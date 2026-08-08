using Shionji.Application;

namespace Shionji.Presentation;

/// <summary>履歴一覧の 1 行。</summary>
public sealed class ActivityItemViewModel(ActivityEntry entry)
{
    public ActivitySeverity Severity { get; } = entry.Severity;

    public string Message { get; } = entry.Message;

    public string Time { get; } = entry.At.ToLocalTime().ToString("HH:mm:ss");
}
