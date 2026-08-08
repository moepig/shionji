using Shionji.Presentation;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class RecordingNotificationService : INotificationService
{
    public List<(string ConfigName, string Message)> Notifications { get; } = [];

    public void NotifyUnexpectedDisconnect(string configName, string message) =>
        Notifications.Add((configName, message));
}

internal sealed class FakeClipboard : IClipboardService
{
    public string? LastText { get; private set; }

    public void SetText(string text) => LastText = text;
}

/// <summary>Application ハーネス + UI フェイクで MainViewModel を組み立てる。</summary>
internal sealed class UiHarness
{
    public Harness App { get; }
    public RecordingNotificationService Notifications { get; } = new();
    public FakeClipboard Clipboard { get; } = new();
    public MainViewModel Main { get; }

    public UiHarness(Shionji.Application.IRetryScheduler? scheduler = null)
    {
        App = new Harness(scheduler);
        Main = new MainViewModel(
            App.Configs,
            App.Supervisor,
            App.Resolution,
            new ImmediateDispatcher(),
            Notifications,
            Clipboard);
    }
}
