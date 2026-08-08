using Shionji.Application;
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
    public ResolutionService Resolution { get; }
    public TunnelSupervisor Supervisor { get; }
    public InMemoryRepository Repository { get; }
    public ConfigService Configs { get; }

    private readonly List<(ConfigId ConfigId, SessionState State)> _events = [];

    public Harness(IRetryScheduler? scheduler = null, InMemoryRepository? repository = null)
    {
        Scheduler = scheduler ?? new ImmediateScheduler();
        Resolution = new ResolutionService(Catalog, Clock);
        Supervisor = new TunnelSupervisor(Catalog, Launcher, Probe, Clock, Scheduler, Resolution);
        Repository = repository ?? new InMemoryRepository();
        Configs = new ConfigService(Repository, Supervisor, Resolution);
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
