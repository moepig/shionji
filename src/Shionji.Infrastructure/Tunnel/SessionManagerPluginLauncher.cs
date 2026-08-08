using System.Diagnostics;
using Amazon.SimpleSystemsManagement.Model;
using Shionji.Domain.Ports;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Aws;

namespace Shionji.Infrastructure.Tunnel;

/// <summary>
/// AWS SDK で ssm:StartSession を呼び、その応答で session-manager-plugin.exe を子プロセス起動する。
/// stdout にポート開通の行が出た時点で成功を返す。
/// </summary>
public sealed class SessionManagerPluginLauncher(
    AwsClientFactory clientFactory,
    SessionManagerPluginLocator locator) : ITunnelLauncher
{
    private static readonly TimeSpan EstablishTimeout = TimeSpan.FromSeconds(30);

    public async Task<Result<ITunnelHandle, ErrorDetail>> LaunchAsync(
        TunnelPlan plan, CancellationToken cancellationToken = default)
    {
        var pluginPath = locator.Locate();
        if (pluginPath.IsFailure)
            return Fail(pluginPath.Error);

        var ssmClient = clientFactory.CreateSsm(plan.Aws);
        if (ssmClient.IsFailure)
            return Fail(ssmClient.Error);

        StartSessionResponse session;
        using (var ssm = ssmClient.Value)
        {
            try
            {
                session = await ssm.StartSessionAsync(
                    new StartSessionRequest
                    {
                        Target = plan.Target.Value,
                        DocumentName = plan.Mode.DocumentName,
                        Parameters = PluginArguments.BuildParameters(plan.Mode, plan.LocalPort),
                        Reason = "Shionji port forwarding",
                    }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Fail(AwsErrors.Classify(ex, FailurePhase.StartSession, plan.Aws.Profile));
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pluginPath.Value,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in PluginArguments.Build(plan, session.SessionId, session.TokenValue, session.StreamUrl))
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var handle = new TunnelProcessHandle(
            process, plan.LocalPort, ct => TerminateSessionAsync(plan.Aws, session.SessionId, ct));

        var established = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            handle.HandleOutput(e.Data, isError: false);
            if (IsEstablishedLine(e.Data))
                established.TrySetResult();
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                handle.HandleOutput(e.Data, isError: true);
        };
        process.Exited += (_, _) =>
        {
            exited.TrySetResult();
            handle.HandleExit();
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            process.Dispose();
            return Fail(new ErrorDetail(
                FailurePhase.Plugin, "PluginStartFailed", $"session-manager-plugin を起動できません: {ex.Message}"));
        }

        WindowsJobObject.TryAssign(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.WhenAny(
            established.Task,
            exited.Task,
            Task.Delay(EstablishTimeout, cancellationToken));

        if (completed == established.Task)
            return Result<ITunnelHandle, ErrorDetail>.Success(handle);

        if (completed == exited.Task)
        {
            var error = new ErrorDetail(
                FailurePhase.Plugin,
                "PluginExited",
                $"session-manager-plugin がポート開通前に終了しました (終了コード {process.ExitCode})。");
            await handle.DisposeAsync();
            return Fail(error);
        }

        // タイムアウト (またはキャンセル)
        await handle.DisposeAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return Fail(new ErrorDetail(
            FailurePhase.Plugin,
            "EstablishTimeout",
            $"{EstablishTimeout.TotalSeconds:0} 秒以内にローカルポートが開きませんでした。"));
    }

    /// <summary>AWS CLI の plugin が出力する開通メッセージ。</summary>
    private static bool IsEstablishedLine(string line) =>
        line.Contains("opened for sessionId", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Waiting for connections", StringComparison.OrdinalIgnoreCase);

    private async Task TerminateSessionAsync(AwsContext aws, string sessionId, CancellationToken cancellationToken)
    {
        var ssmClient = clientFactory.CreateSsm(aws);
        if (ssmClient.IsFailure)
            return;

        using var ssm = ssmClient.Value;
        await ssm.TerminateSessionAsync(new TerminateSessionRequest { SessionId = sessionId }, cancellationToken);
    }

    private static Result<ITunnelHandle, ErrorDetail> Fail(ErrorDetail error) =>
        Result<ITunnelHandle, ErrorDetail>.Failure(error);
}
