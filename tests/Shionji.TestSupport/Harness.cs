using Shionji.Application;
using Microsoft.Extensions.Logging;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.TestSupport;

/// <summary>フェイク一式で構成した Application 層のテストハーネス。</summary>
public sealed class Harness
{
    public FakeCatalog Catalog { get; } = new();
    public FakeLauncher Launcher { get; } = new();
    public FakePortProbe Probe { get; } = new();
    public FakeClock Clock { get; } = new();
    public IRetryScheduler Scheduler { get; }
    public ActivityLog Activity { get; }
    public ResolutionService Resolution { get; }
    public TunnelSupervisor Supervisor { get; }
    public SessionLogStore Logs { get; }
    public InMemoryRepository Repository { get; }
    public ConfigService Configs { get; }

    private readonly List<(ConfigId ConfigId, SessionState State)> _events = [];

    public Harness(IRetryScheduler? scheduler = null, InMemoryRepository? repository = null)
    {
        Scheduler = scheduler ?? new ImmediateScheduler();
        Activity = new ActivityLog(Clock);

        // 各サービスのログをそのまま ActivityLog へ流し、画面表示と同じ経路を再現する
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new ActivityLogProvider(Activity));
        });

        Resolution = new ResolutionService(Catalog, Clock, loggerFactory.CreateLogger<ResolutionService>());
        Supervisor = new TunnelSupervisor(
            Catalog, Launcher, Probe, Clock, Scheduler, Resolution,
            loggerFactory.CreateLogger<TunnelSupervisor>());
        Logs = new SessionLogStore(Supervisor);
        Repository = repository ?? new InMemoryRepository();
        Configs = new ConfigService(
            Repository, Supervisor, Resolution, Logs, loggerFactory.CreateLogger<ConfigService>());
        Supervisor.SessionChanged += (_, e) =>
        {
            lock (_events)
            {
                _events.Add((e.ConfigId, e.State));
            }
        };
    }

    public IReadOnlyList<SessionState> EventsFor(ConfigId id)
    {
        lock (_events)
        {
            return [.. _events.Where(e => e.ConfigId == id).Select(e => e.State)];
        }
    }
}
