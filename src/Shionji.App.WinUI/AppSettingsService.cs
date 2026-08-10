using Microsoft.UI.Xaml;
using Shionji.Infrastructure.Storage;
using Shionji.Presentation;
using Windows.Storage.Pickers;

namespace Shionji.App.WinUI;

/// <summary>
/// 開いているウィンドウにカラーテーマを行き渡らせる。
/// RequestedTheme はウィンドウごとの設定なので、後から開いたウィンドウにも同じ値を適用する。
/// </summary>
public sealed class ThemeHost
{
    private readonly List<Window> _windows = [];

    /// <summary>一度でも適用したか。既定値と同じ指定でも初回は反映させる。</summary>
    private bool _applied;

    public ElementTheme Current { get; private set; } = ElementTheme.Default;

    public void Register(Window window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        Apply(window);
    }

    /// <summary>
    /// テーマの切り替えは ComboBox の選択通知の中から呼ばれる。
    /// その場で視覚ツリーを組み直すと、開いているポップアップごと壊れて中身が消えるため、
    /// 現在の処理が終わってから適用する。
    /// </summary>
    public void Apply(ElementTheme theme)
    {
        if (theme == Current && _applied)
            return;

        Current = theme;
        _applied = true;
        foreach (var window in _windows.ToArray())
        {
            var target = window;
            if (!target.DispatcherQueue.TryEnqueue(() => Apply(target)))
                Apply(target);
        }
    }

    private void Apply(Window window)
    {
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = Current;
    }
}

/// <summary>
/// アプリ設定の読み書き。保存先の指定も含めて appsettings.json 1 本にまとめている。
/// 自動起動だけは OS 側の登録が状態であるため、そちらへ書く。
/// </summary>
public sealed class WinUiAppSettingsService(
    AppSettingsStore settingsStore,
    ThemeHost themeHost,
    WindowsAutoStart autoStart) : IAppSettingsService
{
    public AppTheme Theme => AppThemes.Parse(settingsStore.Current.Theme);

    public StartupOptions Startup => new(
        autoStart.IsEnabled,
        settingsStore.Current.StartMinimized,
        settingsStore.Current.MinimizeToTray,
        settingsStore.Current.HideOnMinimize);

    public string LogDirectory => AppPaths.ResolveLogDirectory(settingsStore.Current);

    public string SettingsFilePath => settingsStore.FilePath;

    public string ConfigsFilePath => AppPaths.ResolveConfigsFilePath(settingsStore.Current);

    public string DefaultLogDirectory => AppPaths.DefaultLogDirectory;

    public string DefaultConfigsDirectory => AppPaths.DefaultDirectory;

    public void PreviewTheme(AppTheme theme) => themeHost.Apply(ThemeOf(theme));

    public IReadOnlyList<string> Save(AppSettingsEdit edit)
    {
        PreviewTheme(edit.Theme);

        // with で複製するので、ここで触っていない設定はそのまま残る
        var problems = settingsStore.Save(settingsStore.Current with
        {
            Theme = AppThemes.ToStorageValue(edit.Theme),
            LogDirectory = AppPaths.NormalizeDirectory(edit.LogDirectory, AppPaths.DefaultLogDirectory),
            ConfigsDirectory = AppPaths.NormalizeDirectory(edit.ConfigsDirectory, AppPaths.DefaultDirectory),
            StartMinimized = edit.Startup.StartMinimized,
            MinimizeToTray = edit.Startup.MinimizeToTray,
            HideOnMinimize = edit.Startup.HideOnMinimize,
        });

        if (autoStart.SetEnabled(edit.Startup.RunAtStartup) is { } problem)
            return [.. problems, problem];

        return problems;
    }

    public static ElementTheme ThemeOf(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}

/// <summary>フォルダ選択ダイアログ。unpackaged では所有ウィンドウの明示が要る。</summary>
public sealed class WinUiFolderPickerService(Func<IntPtr> ownerWindow) : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(string? initialDirectory)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindow());

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception)
        {
            // ダイアログを開けない環境ではテキスト入力で指定してもらう
            return null;
        }
    }
}
