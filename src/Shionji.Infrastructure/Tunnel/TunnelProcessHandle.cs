using System.Diagnostics;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Tunnel;

/// <summary>起動済み session-manager-plugin プロセスへのハンドル。</summary>
internal sealed class TunnelProcessHandle(
    Process process,
    Port localPort,
    string sessionId,
    Func<CancellationToken, Task> terminateSession) : ITunnelHandle
{
    private int _stopped;
    private volatile string? _lastErrorLine;

    // ポートが開くまで LaunchAsync は返らない。その間の plugin 出力は購読前に流れるので溜めておく
    private readonly ReplayingEvent<TunnelExitedEventArgs> _exited = new(maxPending: 1);
    private readonly ReplayingEvent<TunnelLogEventArgs> _log = new();

    public Port LocalPort { get; } = localPort;

    public string SessionId { get; } = sessionId;

    public event EventHandler<TunnelExitedEventArgs>? Exited
    {
        add => _exited.Add(this, value);
        remove => _exited.Remove(value);
    }

    public event EventHandler<TunnelLogEventArgs>? LogEmitted
    {
        add => _log.Add(this, value);
        remove => _log.Remove(value);
    }

    internal void HandleOutput(string line, bool isError)
    {
        if (isError)
            _lastErrorLine = line;
        _log.Raise(this, new TunnelLogEventArgs(line, isError));
    }

    /// <summary>プロセス終了時に呼ばれる。停止要求によるものでなければ予期せぬ終了として通知する。</summary>
    internal void HandleExit()
    {
        if (Volatile.Read(ref _stopped) != 0)
            return;

        int? exitCode = null;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }

        var detail = _lastErrorLine is { Length: > 0 } lastError ? $" {lastError}" : string.Empty;
        _exited.Raise(this, new TunnelExitedEventArgs(new ErrorDetail(
            FailurePhase.Plugin,
            "PluginExited",
            $"session-manager-plugin が予期せず終了しました (終了コード {exitCode?.ToString() ?? "不明"})。{detail}")));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        try
        {
            await terminateSession(cancellationToken);
        }
        catch
        {
            // セッションの後始末は plugin 停止を妨げない
        }

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            // 既に終了している場合など
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        process.Dispose();
    }
}
