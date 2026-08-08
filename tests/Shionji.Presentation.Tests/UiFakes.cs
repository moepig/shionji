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

internal sealed class FakeEditorWindow : IConfigEditorWindowService
{
    public List<ConfigEditorViewModel> Opened { get; } = [];

    public ConfigEditorViewModel Last => Opened[^1];

    public int ClosedCount { get; private set; }

    public void ShowEditor(ConfigEditorViewModel editor)
    {
        Opened.Add(editor);
        editor.Closed += (_, _) => ClosedCount++;
    }
}

internal sealed class FakeFileLocation : IFileLocationService
{
    public string LogDirectory { get; set; } = @"C:\fake\logs";

    public int OpenCount { get; private set; }

    public List<string> OpenedFolders { get; } = [];

    public void OpenLogLocation() => OpenCount++;

    public void OpenFolder(string directory) => OpenedFolders.Add(directory);
}

internal sealed class FakeSettingsWindow : ISettingsWindowService
{
    public List<AppSettingsViewModel> Opened { get; } = [];

    public AppSettingsViewModel Last => Opened[^1];

    public void ShowSettings(AppSettingsViewModel settings) => Opened.Add(settings);
}

internal sealed class FakeFolderPicker : IFolderPickerService
{
    /// <summary>次に「選ばれる」フォルダ。null ならキャンセル扱い。</summary>
    public string? NextFolder { get; set; }

    public List<string?> Requests { get; } = [];

    public Task<string?> PickFolderAsync(string? initialDirectory)
    {
        Requests.Add(initialDirectory);
        return Task.FromResult(NextFolder);
    }
}

internal sealed class FakeAppSettings : IAppSettingsService
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string LogDirectory { get; set; } = @"C:\fake\logs";
    public string SettingsFilePath { get; set; } = @"C:\fake\appsettings.json";
    public string ConfigsFilePath { get; set; } = @"C:\fake\configs.json";
    public string DefaultLogDirectory { get; set; } = @"C:\fake\logs";
    public string DefaultConfigsDirectory { get; set; } = @"C:\fake";

    /// <summary>Save が返す事情。保存はできたが完全には反映できなかった場合に使う。</summary>
    public List<string> Problems { get; } = [];

    public AppTheme? PreviewedTheme { get; private set; }
    public List<AppSettingsEdit> Saved { get; } = [];

    public void PreviewTheme(AppTheme theme) => PreviewedTheme = theme;

    public IReadOnlyList<string> Save(AppSettingsEdit edit)
    {
        Saved.Add(edit);
        Theme = edit.Theme;
        return Problems;
    }
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
    public FakeFileLocation FileLocation { get; } = new();
    public FakeEditorWindow EditorWindow { get; } = new();
    public FakeAppSettings AppSettings { get; } = new();
    public FakeFolderPicker FolderPicker { get; } = new();
    public FakeSettingsWindow SettingsWindow { get; } = new();
    public MainViewModel Main { get; }

    public UiHarness(Shionji.Application.IRetryScheduler? scheduler = null)
        : this(new Harness(scheduler))
    {
    }

    public UiHarness(Harness harness)
    {
        App = harness;
        Main = new MainViewModel(
            App.Configs,
            App.Supervisor,
            App.Resolution,
            new ImmediateDispatcher(),
            Notifications,
            Clipboard,
            SsoLogin,
            App.Logs,
            App.Activity,
            FileLocation,
            EditorWindow,
            App.Catalog,
            new AppSettingsContext(AppSettings, FolderPicker, SettingsWindow));
    }
}
