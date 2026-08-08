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

/// <summary>アプリ設定の読み書き。保存先の指定は locations.json、それ以外は appsettings.json。</summary>
public sealed class WinUiAppSettingsService(
    AppSettingsStore settingsStore,
    StorageLocationsStore locationsStore,
    ThemeHost themeHost) : IAppSettingsService
{
    public AppTheme Theme => ParseTheme(settingsStore.Current.Theme);

    public string LogDirectory => locationsStore.Current.ResolvedLogDirectory;

    public string SettingsFilePath => locationsStore.Current.SettingsFilePath;

    public string ConfigsFilePath => locationsStore.Current.ConfigsFilePath;

    public string BootstrapFilePath => locationsStore.BootstrapPath;

    public void PreviewTheme(AppTheme theme) => themeHost.Apply(theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    });

    public IReadOnlyList<string> Save(
        AppTheme theme, string logDirectory, string settingsDirectory, string configsDirectory)
    {
        PreviewTheme(theme);

        List<string> problems = [];

        var settings = settingsStore.Current;
        settings.Theme = theme.ToString();
        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"アプリ設定を書き込めません: {ex.Message}");
        }

        // 既定と同じフォルダなら上書き指定を持たない (既定が変わったときに追従できる)
        problems.AddRange(locationsStore.Save(new StorageLocations
        {
            LogDirectory = Normalize(logDirectory, StorageLocations.DefaultLogDirectory),
            SettingsDirectory = Normalize(settingsDirectory, StorageLocations.DefaultDirectory),
            ConfigsDirectory = Normalize(configsDirectory, StorageLocations.DefaultDirectory),
        }));

        return problems;
    }

    private static string? Normalize(string directory, string defaultDirectory)
    {
        var trimmed = directory.Trim().TrimEnd('\\', '/');
        if (trimmed.Length == 0)
            return null;
        return string.Equals(trimmed, defaultDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    public static AppTheme ParseTheme(string? value) =>
        Enum.TryParse<AppTheme>(value, ignoreCase: true, out var parsed) ? parsed : AppTheme.System;
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
