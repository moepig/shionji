using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shionji.Presentation;

/// <summary>アプリ設定ウィンドウ。表示 / ログ / 設定 の 3 節。</summary>
public sealed partial class AppSettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settings;
    private readonly IFolderPickerService _folderPicker;
    private readonly IFileLocationService _fileLocation;
    private readonly AppTheme _themeOnOpen;

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
    }

    /// <summary>
    /// アプリ設定ファイルの絶対パス。ここに保存先の指定も入るため、
    /// このファイル自身は動かせない。表示のみ。
    /// </summary>
    public string SettingsFilePath => _settings.SettingsFilePath;

    public event EventHandler? Closed;

    // --- 表示 ---

    /// <summary>選んだ瞬間に見た目へ反映する (保存しないで閉じたら元に戻す)。</summary>
    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    partial void OnThemeChanged(AppTheme value) => _settings.PreviewTheme(value);

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
        var moved =
            !SamePath(LogDirectory, _settings.LogDirectory)
            || !SamePath(ConfigsDirectory, DirectoryOf(_settings.ConfigsFilePath));

        var problems = _settings.Save(Theme, LogDirectory, ConfigsDirectory);

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
    private void Cancel()
    {
        // 見た目だけ変えて閉じた場合は元に戻す
        _settings.PreviewTheme(_themeOnOpen);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<string?> PickAsync(string current)
    {
        var picked = await _folderPicker.PickFolderAsync(current.Length > 0 ? current : null);
        return picked;
    }

    private static string DirectoryOf(string filePath) => Path.GetDirectoryName(filePath) ?? string.Empty;

    private static string FileNameOf(string filePath) => Path.GetFileName(filePath);

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
