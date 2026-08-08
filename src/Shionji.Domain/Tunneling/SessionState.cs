using Shionji.Domain.Resolution;

namespace Shionji.Domain.Tunneling;

/// <summary>トンネルセッションの状態。</summary>
public abstract record SessionState
{
    private SessionState() { }

    /// <summary>未接続。</summary>
    public sealed record Idle : SessionState
    {
        public static readonly Idle Instance = new();
    }

    /// <summary>リソース解決中。</summary>
    public sealed record Resolving : SessionState
    {
        public static readonly Resolving Instance = new();
    }

    /// <summary>SSM セッション起動中 (plugin がまだローカルポートを開いていない)。</summary>
    public sealed record Starting(TunnelPlan Plan) : SessionState;

    /// <summary>トンネル確立済み。</summary>
    public sealed record Established(TunnelPlan Plan, DateTimeOffset Since) : SessionState;

    /// <summary>切断処理中。</summary>
    public sealed record Closing : SessionState
    {
        public static readonly Closing Instance = new();
    }

    /// <summary>予期せぬ切断後、再試行待ち。</summary>
    public sealed record Reconnecting(int Attempt, TimeSpan Delay, ErrorDetail Cause) : SessionState;

    /// <summary>失敗。エラー詳細を保持する。</summary>
    public sealed record Failed(ErrorDetail Error) : SessionState;
}
