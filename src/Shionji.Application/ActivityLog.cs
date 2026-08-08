using Microsoft.Extensions.Logging;
using Shionji.Domain.Ports;

namespace Shionji.Application;

public enum ActivitySeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ActivityEntry(DateTimeOffset At, ActivitySeverity Severity, string Message);

/// <summary>
/// 画面に出す直近の動作履歴。ログ出力と同じ流れを受け取り、
/// ステータスバーの表示と履歴一覧の元になる。
/// </summary>
public sealed class ActivityLog(IClock clock)
{
    private const int MaxEntries = 200;

    private readonly object _sync = new();
    private readonly List<ActivityEntry> _entries = [];

    public event EventHandler<ActivityEntry>? Posted;

    /// <summary>最新の 1 件。まだ何もなければ null。</summary>
    public ActivityEntry? Latest
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count > 0 ? _entries[^1] : null;
            }
        }
    }

    /// <summary>古い順の履歴 (最大 200 件)。</summary>
    public IReadOnlyList<ActivityEntry> Recent
    {
        get
        {
            lock (_sync)
            {
                return [.. _entries];
            }
        }
    }

    public void Post(ActivitySeverity severity, string message)
    {
        var entry = new ActivityEntry(clock.UtcNow, severity, message);
        lock (_sync)
        {
            _entries.Add(entry);
            while (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }

        Posted?.Invoke(this, entry);
    }
}

/// <summary>
/// ILogger の出力を <see cref="ActivityLog"/> へ流す。
/// ファイルに残る内容と同じものが画面のステータスバーにも出るようにするための橋渡し。
/// </summary>
public sealed class ActivityLogProvider(ActivityLog activityLog) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ActivityLogger(activityLog);

    public void Dispose()
    {
    }

    private sealed class ActivityLogger(ActivityLog activityLog) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            // 画面に出すのは要約のみ。スタックトレースはファイルログ側で追う
            activityLog.Post(SeverityOf(logLevel), formatter(state, exception));
        }

        private static ActivitySeverity SeverityOf(LogLevel level) => level switch
        {
            LogLevel.Warning => ActivitySeverity.Warning,
            LogLevel.Error or LogLevel.Critical => ActivitySeverity.Error,
            _ => ActivitySeverity.Info,
        };
    }
}
