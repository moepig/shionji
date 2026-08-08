namespace Shionji.Domain.Tunneling;

/// <summary>自動再接続の指数バックオフ方針。</summary>
public sealed record ReconnectPolicy(TimeSpan BaseDelay, TimeSpan MaxDelay, int MaxAttempts)
{
    /// <summary>2s → 4s → 8s → 16s → 30s、上限 5 回。</summary>
    public static readonly ReconnectPolicy Default =
        new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), MaxAttempts: 5);

    /// <summary>attempt 回目 (1 始まり) の再試行までの待機時間。</summary>
    public TimeSpan DelayFor(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        var delay = BaseDelay * Math.Pow(2, attempt - 1);
        return delay > MaxDelay ? MaxDelay : delay;
    }
}
