using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Ports;

public sealed class TunnelExitedEventArgs(ErrorDetail error) : EventArgs
{
    public ErrorDetail Error { get; } = error;
}

public sealed class TunnelLogEventArgs(string line, bool isError) : EventArgs
{
    public string Line { get; } = line;
    public bool IsError { get; } = isError;
}

/// <summary>起動済みトンネルへのハンドル。</summary>
public interface ITunnelHandle : IAsyncDisposable
{
    /// <summary>実際に待ち受けているローカルポート。</summary>
    Port LocalPort { get; }

    /// <summary>トンネルが予期せず終了した (StopAsync による停止では発火しない)。</summary>
    event EventHandler<TunnelExitedEventArgs>? Exited;

    /// <summary>plugin の出力 1 行ごとに発火する。</summary>
    event EventHandler<TunnelLogEventArgs>? LogEmitted;

    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>トンネル計画から SSM セッションを起動する。ローカルポートが開いた時点で成功を返す。</summary>
public interface ITunnelLauncher
{
    Task<Result<ITunnelHandle, ErrorDetail>> LaunchAsync(
        TunnelPlan plan,
        CancellationToken cancellationToken = default);
}
