using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shionji.Presentation;

/// <summary>アプリ設定ウィンドウの大項目。左のメニューで切り替える。</summary>
public enum SettingsSection
{
    Display,
    Startup,
    Log,
    Files,
}

/// <summary>アプリ設定ウィンドウ。表示 / 起動 / ログ / 設定 を大項目として切り替える。</summary>
public sealed partial class AppSettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settings;
    private readonly IFolderPickerService _folderPicker;
    private readonly IFileLocationService _fileLocation;
    private readonly AppTheme _themeOnOpen;

    /// <summary>保存済みか。閉じたときに試用中のテーマを戻すかどうかの判断に使う。</summary>
    private bool _themeSaved;

    public AppSettingsViewModel(
        IAppSettingsService settings,
        IFolderPickerService folderPicker,
        IFileLocationService fileLocation)
    {
        _settings = settings;
        _folderPicker = folderPicker;
        _fileLocation = fileLocation;

        _themeOnOpen = settings.Theme;
        Theme = settings.Theme;
        LogDirectory = settings.LogDirectory;
        ConfigsDirectory = DirectoryOf(settings.ConfigsFilePath);

        RunAtStartup = settings.Startup.RunAtStartup;
        StartMinimized = settings.Startup.StartMinimized;
        MinimizeToTray = settings.Startup.MinimizeToTray;
        HideOnMinimize = settings.Startup.HideOnMinimize;
    }

    /// <summary>
    /// アプリ設定ファイルの絶対パス。ここに保存先の指定も入るため、
    /// このファイル自身は動かせない。表示のみ。
    /// </summary>
    public string SettingsFilePath => _settings.SettingsFilePath;

    public event EventHandler? Closed;

    // --- 大項目の切り替え ---

    /// <summary>いま開いている大項目。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisplaySection))]
    [NotifyPropertyChangedFor(nameof(IsStartupSection))]
    [NotifyPropertyChangedFor(nameof(IsLogSection))]
    [NotifyPropertyChangedFor(nameof(IsFilesSection))]
    [NotifyPropertyChangedFor(nameof(SectionTitle))]
    public partial SettingsSection Section { get; set; } = SettingsSection.Display;

    public bool IsDisplaySection => Section == SettingsSection.Display;
    public bool IsStartupSection => Section == SettingsSection.Startup;
    public bool IsLogSection => Section == SettingsSection.Log;
    public bool IsFilesSection => Section == SettingsSection.Files;

    public string SectionTitle => Section switch
    {
        SettingsSection.Display => "表示",
        SettingsSection.Startup => "起動",
        SettingsSection.Log => "ログ",
        SettingsSection.Files => "設定",
        _ => string.Empty,
    };

    // --- 表示 ---

    /// <summary>選んだ瞬間に見た目へ反映する (保存しないで閉じたら元に戻す)。</summary>
    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    partial void OnThemeChanged(AppTheme value) => _settings.PreviewTheme(value);

    // --- 起動 ---

    /// <summary>Windows へのサインイン時に自動起動する。</summary>
    [ObservableProperty]
    public partial bool RunAtStartup { get; set; }

    /// <summary>起動時はウィンドウを出さず、タスクトレイへ格納した状態で始める。</summary>
    [ObservableProperty]
    public partial bool StartMinimized { get; set; }

    /// <summary>ウィンドウを閉じたときに終了せず、タスクトレイへ格納する。</summary>
    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    /// <summary>ウィンドウを最小化したときに、タスクバーではなくタスクトレイへ格納する。</summary>
    [ObservableProperty]
    public partial bool HideOnMinimize { get; set; }

    // --- ログ / 設定 ---

    /// <summary>ログの保存先フォルダ。空なら既定。</summary>
    [ObservableProperty]
    public partial string LogDirectory { get; set; } = string.Empty;

    /// <summary>接続先設定ファイルを置くフォルダ。空なら既定。</summary>
    [ObservableProperty]
    public partial string ConfigsDirectory { get; set; } = string.Empty;

    public string ConfigsFileName => FileNameOf(_settings.ConfigsFilePath);

    /// <summary>保存できたが完全には反映できなかった事情。空なら何も出さない。</summary>
    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty]
    public partial bool HasProblems { get; set; }

    /// <summary>保存先を変えたので再起動が要る。</summary>
    [ObservableProperty]
    public partial bool NeedsRestart { get; set; }

    [RelayCommand]
    private async Task BrowseLogAsync() =>
        LogDirectory = await PickAsync(LogDirectory) ?? LogDirectory;

    [RelayCommand]
    private async Task BrowseConfigsAsync() =>
        ConfigsDirectory = await PickAsync(ConfigsDirectory) ?? ConfigsDirectory;

    [RelayCommand]
    private void OpenLog() => _fileLocation.OpenFolder(LogDirectory);

    [RelayCommand]
    private void OpenSettings() => _fileLocation.OpenFolder(DirectoryOf(_settings.SettingsFilePath));

    [RelayCommand]
    private void OpenConfigs() => _fileLocation.OpenFolder(ConfigsDirectory);

    [RelayCommand]
    private void Save()
    {
        // 空欄は「既定に戻す」の意味。既に既定ならば変更ではないので、再起動を促さない
        var moved =
            !SamePath(Resolve(LogDirectory, _settings.DefaultLogDirectory), _settings.LogDirectory)
            || !SamePath(
                Resolve(ConfigsDirectory, _settings.DefaultConfigsDirectory),
                DirectoryOf(_settings.ConfigsFilePath));

        var problems = _settings.Save(new AppSettingsEdit(
            Theme,
            LogDirectory,
            ConfigsDirectory,
            new StartupOptions(RunAtStartup, StartMinimized, MinimizeToTray, HideOnMinimize)));
        _themeSaved = true;

        Problems.Clear();
        foreach (var problem in problems)
            Problems.Add(problem);
        HasProblems = Problems.Count > 0;
        NeedsRestart = moved;

        // 伝えることがある間は開いたままにする (閉じると気づかれない)
        if (!HasProblems && !NeedsRestart)
            Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// ウィンドウが閉じられた。キャンセル・× のどちらで閉じても、
    /// 保存していないテーマの試用は開いたときの状態に戻す。
    /// </summary>
    public void HandleWindowClosed()
    {
        if (!_themeSaved && Theme != _themeOnOpen)
            _settings.PreviewTheme(_themeOnOpen);
    }

    private async Task<string?> PickAsync(string current)
    {
        var picked = await _folderPicker.PickFolderAsync(current.Length > 0 ? current : null);
        return picked;
    }

    private static string DirectoryOf(string filePath) => Path.GetDirectoryName(filePath) ?? string.Empty;

    private static string FileNameOf(string filePath) => Path.GetFileName(filePath);

    /// <summary>空欄は既定のフォルダを指す。</summary>
    private static string Resolve(string input, string fallback) =>
        input.Trim().Length == 0 ? fallback : input;

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
