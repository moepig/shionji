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

internal sealed class FakeSsoLogin : Shionji.Domain.Ports.ISsoLoginService
{
    public int Calls { get; private set; }

    /// <summary>ログイン失敗を再現する場合に設定。</summary>
    public Shionji.Domain.Resolution.ErrorDetail? Result { get; set; }

    /// <summary>ログイン成功の副作用 (カタログの挙動切替など)。</summary>
    public Action? OnLogin { get; set; }

    public Task<Shionji.Domain.Resolution.ErrorDetail?> LoginAsync(
        Shionji.Domain.ValueObjects.ProfileName profile, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Result is null)
            OnLogin?.Invoke();
        return Task.FromResult(Result);
    }
}

/// <summary>Application ハーネス + UI フェイクで MainViewModel を組み立てる。</summary>
internal sealed class UiHarness
{
    public Harness App { get; }
    public RecordingNotificationService Notifications { get; } = new();
    public FakeClipboard Clipboard { get; } = new();
    public FakeSsoLogin SsoLogin { get; } = new();
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
            Clipboard,
            SsoLogin,
            App.Logs);
    }
}
