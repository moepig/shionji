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

/// <summary>
/// 登録済みコマンドを起動する。起動するだけで、終了は待たない。
/// </summary>
public interface IExternalCommandLauncher
{
    /// <summary>起動できなかった場合はその理由を返す。起動できた場合は null。</summary>
    string? Launch(string fileName, string arguments);
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

/// <summary>テーマと保存文字列の相互変換。設定ファイルを手で編集されても落ちない。</summary>
public static class AppThemes
{
    public static AppTheme Parse(string? value) =>
        Enum.TryParse<AppTheme>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : AppTheme.System;

    public static string ToStorageValue(AppTheme theme) => theme.ToString();
}

/// <summary>起動・最小化・終了の扱い。真偽値が並ぶ取り違えを防ぐためまとめる。</summary>
/// <param name="RunAtStartup">Windows へのサインイン時にアプリを自動起動する。</param>
/// <param name="StartMinimized">起動時にウィンドウを出さず、タスクトレイへ格納した状態で始める。</param>
/// <param name="HideOnMinimize">ウィンドウを最小化したときに、タスクバーではなくタスクトレイへ格納する。</param>
/// <param name="ConfirmOnExit">終了する前に確認を出す。</param>
public sealed record StartupOptions(
    bool RunAtStartup,
    bool StartMinimized,
    bool HideOnMinimize,
    bool ConfirmOnExit);

/// <summary>設定ウィンドウで編集できる内容。同じ型の引数が並ぶ取り違えを防ぐためまとめる。</summary>
public sealed record AppSettingsEdit(
    AppTheme Theme,
    string LogDirectory,
    string ConfigsDirectory,
    StartupOptions Startup);

/// <summary>アプリ設定 (接続先設定とは別) の読み書きとテーマ適用。</summary>
public interface IAppSettingsService
{
    AppTheme Theme { get; }

    /// <summary>起動と格納の扱い。自動起動は OS 側の登録状態を指す。</summary>
    StartupOptions Startup { get; }

    /// <summary>ログの保存先フォルダ。</summary>
    string LogDirectory { get; }

    /// <summary>アプリ設定ファイルの絶対パス。置き場所は固定なので表示のみ。</summary>
    string SettingsFilePath { get; }

    /// <summary>接続先設定ファイルの絶対パス。</summary>
    string ConfigsFilePath { get; }

    /// <summary>指定が無いときのログの保存先。空欄と同じ意味かどうかの判定に使う。</summary>
    string DefaultLogDirectory { get; }

    /// <summary>指定が無いときの接続先設定の保存先。</summary>
    string DefaultConfigsDirectory { get; }

    /// <summary>テーマを即座に適用する (保存は Save で行う)。</summary>
    void PreviewTheme(AppTheme theme);

    /// <summary>
    /// 保存する。フォルダの指定は空文字で既定に戻す。
    /// 保存はできたが完全には反映できなかった事情 (ファイルを移せなかったなど) を返す。
    /// </summary>
    IReadOnlyList<string> Save(AppSettingsEdit edit);
}
