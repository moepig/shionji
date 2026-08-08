using Shionji.Application;
using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.TestSupport;

public sealed class FakeCatalog : IResourceCatalog
{
    public Func<AwsContext, ResourceQuery, ResolutionOutcome> Handler { get; set; } = DefaultHandler;

    /// <summary>クエリ型ごとにもっともらしいリソースを返す既定ハンドラ。</summary>
    public static ResolutionOutcome DefaultHandler(AwsContext aws, ResourceQuery query) => query switch
    {
        ElastiCacheQuery => new ResolutionOutcome.Resolved(
            TestData.Resource("cache-1", host: "redis.prod.example.com", defaultPort: 6379)),
        AuroraQuery => new ResolutionOutcome.Resolved(
            TestData.Resource("aurora-1", host: "cluster.rds.example.com", defaultPort: 5432)),
        Ec2Query => new ResolutionOutcome.Resolved(
            TestData.Resource("ec2-1", host: "10.0.1.5", ssmTarget: "i-0feedfacefeedface")),
        EcsTaskQuery => new ResolutionOutcome.Resolved(
            TestData.Resource("task-1", host: "10.0.3.7", ssmTarget: "ecs:cluster_task_runtime")),
        _ => ResolutionOutcome.NotFound.Instance,
    };

    public int CallCount { get; private set; }
    public List<ResourceQuery> Queries { get; } = [];

    public Task<ResolutionOutcome> ResolveAsync(
        AwsContext aws, ResourceQuery query, FailurePhase phase, CancellationToken cancellationToken = default)
    {
        CallCount++;
        Queries.Add(query);
        return Task.FromResult(Handler(aws, query));
    }
}

public sealed class FakeHandle(Port localPort) : ITunnelHandle
{
    public Port LocalPort { get; } = localPort;
    public bool Stopped { get; private set; }

    public event EventHandler<TunnelExitedEventArgs>? Exited;
    public event EventHandler<TunnelLogEventArgs>? LogEmitted;

    public void TriggerExit(ErrorDetail error) => Exited?.Invoke(this, new TunnelExitedEventArgs(error));

    public void EmitLog(string line, bool isError = false) =>
        LogEmitted?.Invoke(this, new TunnelLogEventArgs(line, isError));

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Stopped = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeLauncher : ITunnelLauncher
{
    /// <summary>計画を受け取り成否を決める。既定は常に成功。</summary>
    public Func<TunnelPlan, Result<ITunnelHandle, ErrorDetail>>? Handler { get; set; }

    public int LaunchCount { get; private set; }
    public List<TunnelPlan> Plans { get; } = [];
    public List<FakeHandle> Handles { get; } = [];

    public Task<Result<ITunnelHandle, ErrorDetail>> LaunchAsync(
        TunnelPlan plan, CancellationToken cancellationToken = default)
    {
        LaunchCount++;
        Plans.Add(plan);

        if (Handler is { } handler)
            return Task.FromResult(handler(plan));

        var handle = new FakeHandle(plan.LocalPort);
        Handles.Add(handle);
        return Task.FromResult(Result<ITunnelHandle, ErrorDetail>.Success(handle));
    }

    public FakeHandle LastHandle => Handles[^1];
}

public sealed class FakePortProbe : ILocalPortProbe
{
    public HashSet<int> BusyPorts { get; } = [];
    public int NextFreePort { get; set; } = 50000;

    public bool IsAvailable(Port port) => !BusyPorts.Contains(port.Value);

    public Result<Port, ErrorDetail> AcquireFreePort() =>
        Result<Port, ErrorDetail>.Success(Port.Create(NextFreePort).Value);
}

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
}

/// <summary>待機せず即座に完了するスケジューラ。要求された待機時間を記録する。</summary>
public sealed class ImmediateScheduler : IRetryScheduler
{
    public List<TimeSpan> Delays { get; } = [];

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}

/// <summary>Release されるまで待機し続けるスケジューラ。再試行待ち中の挙動を検証する。</summary>
public sealed class BlockingScheduler : IRetryScheduler
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<TimeSpan> Delays { get; } = [];

    public void Release() => _gate.TrySetResult();

    public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Delays.Add(delay);
        await _gate.Task.WaitAsync(cancellationToken);
    }
}

public sealed class InMemoryRepository : IForwardingConfigRepository
{
    private readonly Dictionary<ConfigId, ForwardingConfig> _store = [];

    public InMemoryRepository(params ForwardingConfig[] configs)
    {
        foreach (var config in configs)
            _store[config.Id] = config;
    }

    public int SaveCount { get; private set; }

    public Task<IReadOnlyList<ForwardingConfig>> LoadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ForwardingConfig>>([.. _store.Values]);

    public Task SaveAsync(ForwardingConfig config, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        _store[config.Id] = config;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ConfigId id, CancellationToken cancellationToken = default)
    {
        _store.Remove(id);
        return Task.CompletedTask;
    }
}
