using Shionji.Application;
using Microsoft.Extensions.Logging;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.TestSupport;

/// <summary>テキストログに出る 1 行分 (要約 + 詳細フィールド)。</summary>
public sealed record WrittenLog(LogLevel Level, string Summary, IReadOnlyDictionary<string, string> Details)
{
    public string? Detail(string key) => Details.GetValueOrDefault(key);
}

/// <summary>詳細フィールドまで含めてログを記録するテスト用プロバイダ。</summary>
internal sealed class RecordingLogProvider(List<WrittenLog> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Recorder(sink);

    public void Dispose()
    {
    }

    private sealed class Recorder(List<WrittenLog> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var details = state is Domain.Diagnostics.IDetailedLogState detailed
                ? detailed.Details.ToDictionary(d => d.Key, d => d.Value?.ToString() ?? string.Empty)
                : [];

            lock (sink)
            {
                sink.Add(new WrittenLog(logLevel, formatter(state, exception), details));
            }
        }
    }
}

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
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>ActivityLog へ流れるロガー。ハーネス外のサービスに渡すときに使う。</summary>
    public ILogger<T> LoggerFor<T>() => _loggerFactory.CreateLogger<T>();

    /// <summary>テキストログ相当 (詳細フィールド込み) の記録。</summary>
    public IReadOnlyList<WrittenLog> Written
    {
        get
        {
            lock (_written)
            {
                return [.. _written];
            }
        }
    }

    /// <summary>要約に指定の語を含む最後のログ行。</summary>
    public WrittenLog WrittenWith(string fragment) =>
        Written.Last(w => w.Summary.Contains(fragment));

    private readonly List<WrittenLog> _written = [];

    public Harness(IRetryScheduler? scheduler = null, InMemoryRepository? repository = null)
    {
        Scheduler = scheduler ?? new ImmediateScheduler();
        Activity = new ActivityLog(Clock);

        // 各サービスのログをそのまま ActivityLog へ流し、画面表示と同じ経路を再現する
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new ActivityLogProvider(Activity));
            builder.AddProvider(new RecordingLogProvider(_written));
        });

        Resolution = new ResolutionService(Catalog, Clock, _loggerFactory.CreateLogger<ResolutionService>());
        Supervisor = new TunnelSupervisor(
            Catalog, Launcher, Probe, Clock, Scheduler, Resolution,
            _loggerFactory.CreateLogger<TunnelSupervisor>());
        Logs = new SessionLogStore(Supervisor);
        Repository = repository ?? new InMemoryRepository();
        Configs = new ConfigService(
            Repository, Supervisor, Resolution, Logs, _loggerFactory.CreateLogger<ConfigService>());
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
