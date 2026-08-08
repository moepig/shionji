namespace Shionji.Application;

/// <summary>再接続バックオフの待機。テストで即時化できるよう抽象化する。</summary>
public interface IRetryScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class TaskDelayRetryScheduler : IRetryScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
