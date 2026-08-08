namespace Shionji.Presentation;

/// <summary>UI スレッドへの処理の投げ込み。WinUI 側で DispatcherQueue により実装する。</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>トースト通知。WinUI 側で AppNotification により実装する。</summary>
public interface INotificationService
{
    void NotifyUnexpectedDisconnect(string configName, string message);
}

/// <summary>クリップボード。</summary>
public interface IClipboardService
{
    void SetText(string text);
}

/// <summary>接続先設定の追加 / 編集を別ウィンドウで開く。</summary>
public interface IConfigEditorWindowService
{
    void ShowEditor(ConfigEditorViewModel editor);
}

/// <summary>アプリ設定を別ウィンドウで開く。</summary>
public interface ISettingsWindowService
{
    void ShowSettings(AppSettingsViewModel settings);
}

/// <summary>アプリ設定ウィンドウを開くのに要る一式。MainViewModel の引数を膨らませないためにまとめる。</summary>
public sealed record AppSettingsContext(
    IAppSettingsService Settings,
    IFolderPickerService FolderPicker,
    ISettingsWindowService Window);

/// <summary>ファイルの所在をユーザーに示す (エクスプローラーで開くなど)。</summary>
public interface IFileLocationService
{
    /// <summary>表示用のログ保存先パス。</summary>
    string LogDirectory { get; }

    void OpenLogLocation();

    /// <summary>任意のフォルダをエクスプローラーで開く。</summary>
    void OpenFolder(string directory);
}

/// <summary>保存先フォルダをユーザーに選ばせる。キャンセルなら null。</summary>
public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(string? initialDirectory);
}

/// <summary>カラーテーマ。System は OS の設定に従う。</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>アプリ設定 (接続先設定とは別) の読み書きとテーマ適用。</summary>
public interface IAppSettingsService
{
    AppTheme Theme { get; }

    /// <summary>ログの保存先フォルダ。</summary>
    string LogDirectory { get; }

    /// <summary>アプリ設定ファイルの絶対パス。置き場所は固定なので表示のみ。</summary>
    string SettingsFilePath { get; }

    /// <summary>接続先設定ファイルの絶対パス。</summary>
    string ConfigsFilePath { get; }

    /// <summary>テーマを即座に適用する (保存は Save で行う)。</summary>
    void PreviewTheme(AppTheme theme);

    /// <summary>
    /// 保存する。フォルダの指定は空文字で既定に戻す。
    /// 保存はできたが完全には反映できなかった事情 (ファイルを移せなかったなど) を返す。
    /// </summary>
    IReadOnlyList<string> Save(AppTheme theme, string logDirectory, string configsDirectory);
}
