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
    SessionManagerPluginLocator locator,
    ILocalPortProbe portProbe) : ITunnelLauncher
{
    private static readonly TimeSpan EstablishTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ListenPollInterval = TimeSpan.FromMilliseconds(100);

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
                return Fail(AwsErrors.Classify(
                    ex, FailurePhase.StartSession, plan.Aws.Profile, clientFactory.IsSsoProfile(plan.Aws.Profile)));
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

        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                handle.HandleOutput(e.Data, isError: false);
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

        var established = await WaitForListeningAsync(plan.LocalPort, exited.Task, cancellationToken);
        if (established)
            return Result<ITunnelHandle, ErrorDetail>.Success(handle);

        if (exited.Task.IsCompleted)
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

    /// <summary>
    /// ローカルポートが実際に listen 状態になるのを待つ。
    /// plugin の出力文言はバージョンで変わりうるため、確立の判定はポートの状態で行う。
    /// </summary>
    private async Task<bool> WaitForListeningAsync(
        Port localPort, Task exited, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EstablishTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (portProbe.IsListening(localPort))
                return true;

            if (exited.IsCompleted)
            {
                // 終了直前に開通していた可能性を最後に一度だけ確認する
                return portProbe.IsListening(localPort);
            }

            await Task.Delay(ListenPollInterval, cancellationToken);
        }

        return false;
    }

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
