using Shionji.Domain.Ports;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Fakes;

/// <summary>
/// デモモード用の ITunnelLauncher。数秒で確立し、ログを流す。
/// 転送先ホストに flaky を含むトンネルは確立の約 10 秒後に疑似切断する (自動再接続のデモ)。
/// プロファイル expired-sso は資格情報エラーを再現する。
/// </summary>
public sealed class FakeTunnelLauncher(FakeSsoState? ssoState = null) : ITunnelLauncher
{
    public async Task<Result<ITunnelHandle, ErrorDetail>> LaunchAsync(
        TunnelPlan plan, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Random.Shared.Next(1000, 1800), cancellationToken);

        if (plan.Aws.Profile.Value == "expired-sso" && ssoState?.IsLoggedIn(plan.Aws.Profile.Value) != true)
        {
            return Result<ITunnelHandle, ErrorDetail>.Failure(new ErrorDetail(
                FailurePhase.Credentials,
                "SsoLoginRequired",
                $"プロファイル「{plan.Aws.Profile.Value}」の認証情報が期限切れです。" +
                $"`aws sso login --profile {plan.Aws.Profile.Value}` を実行してください。"));
        }

        var flaky = plan.Mode is SessionMode.RemoteHostForward remote &&
                    remote.Host.Value.Contains("flaky", StringComparison.OrdinalIgnoreCase);
        var handle = new FakeTunnelHandle(plan.LocalPort, flaky);
        handle.Begin();
        return Result<ITunnelHandle, ErrorDetail>.Success(handle);
    }

    private sealed class FakeTunnelHandle(Port localPort, bool flaky) : ITunnelHandle
    {
        private readonly CancellationTokenSource _cts = new();

        public Port LocalPort { get; } = localPort;

        public string SessionId { get; } = $"s-demo{Random.Shared.Next(100000, 999999)}";

        public event EventHandler<TunnelExitedEventArgs>? Exited;
        public event EventHandler<TunnelLogEventArgs>? LogEmitted;

        public void Begin()
        {
            LogEmitted?.Invoke(this, new TunnelLogEventArgs(
                $"Port {LocalPort.Value} opened for sessionId demo-session (fake).", false));

            if (flaky)
                _ = DropLaterAsync();
        }

        private async Task DropLaterAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            LogEmitted?.Invoke(this, new TunnelLogEventArgs("Connection reset by peer (fake).", true));
            Exited?.Invoke(this, new TunnelExitedEventArgs(new ErrorDetail(
                FailurePhase.Plugin, "PluginExited", "トンネルが予期せず切断されました (デモ)。")));
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _cts.Cancel();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
