using System.Text.Json;

namespace Shionji.Infrastructure.Storage;

/// <summary>
/// アプリ全体の設定 (接続先設定とは別)。
/// 一部だけを書き換えるときは <c>current with { … }</c> とする。
/// 手で全項目を写すと、項目が増えたときに黙って既定値へ戻る事故が起きる。
/// </summary>
public sealed record AppSettings
{
    /// <summary>session-manager-plugin.exe の上書きパス。null なら自動探索。</summary>
    public string? PluginPath { get; set; }

    /// <summary>AWS API のエンドポイント上書き (VPC エンドポイントなど)。null なら通常のリージョン解決。</summary>
    public string? AwsEndpointOverride { get; set; }

    /// <summary>ウィンドウを最小化したときにタスクトレイへ格納する。</summary>
    public bool HideOnMinimize { get; set; } = true;

    /// <summary>起動時にウィンドウを出さず、タスクトレイへ格納した状態で始める。</summary>
    public bool StartMinimized { get; set; }

    /// <summary>終了する前に確認を出す。</summary>
    public bool ConfirmOnExit { get; set; } = true;

    /// <summary>ログファイルの保持日数。監査要件に応じて延ばす。</summary>
    public int LogRetentionDays { get; set; } = 30;

    /// <summary>カラーテーマ。System / Light / Dark。</summary>
    public string Theme { get; set; } = "System";

    /// <summary>ログファイルを置くフォルダ。null なら既定。</summary>
    public string? LogDirectory { get; set; }

    /// <summary>接続先設定ファイルを置くフォルダ。null なら既定。</summary>
    public string? ConfigsDirectory { get; set; }
}

/// <summary>%APPDATA%/Shionji/appsettings.json の読み書き。この置き場所は固定。</summary>
public sealed class AppSettingsStore(string? filePath = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath = filePath ?? AppPaths.SettingsFilePath;

    /// <summary>実際に読み書きしているファイル。</summary>
    public string FilePath => _filePath;

    public AppSettings Current { get; private set; } = new();

    /// <summary>
    /// 読めない場合は既定値で続行する。設定ファイルの不備でアプリが起動できなくなる方が害が大きい。
    /// </summary>
    public AppSettings Load()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions)
                    ?? new AppSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Current = new AppSettings();
            }
        }

        return Current;
    }

    /// <summary>
    /// 保存する。接続先設定の保存先が変わっていれば既存ファイルも移す。
    /// 保存はできたが完全には反映できなかった事情を返す。
    /// </summary>
    public IReadOnlyList<string> Save(AppSettings settings)
    {
        List<string> problems = [];

        StorageRelocation.MoveIfNeeded(
            AppPaths.ResolveConfigsFilePath(Current),
            AppPaths.ResolveConfigsFilePath(settings),
            "接続先設定ファイル",
            problems);
        StorageRelocation.EnsureDirectory(AppPaths.ResolveLogDirectory(settings), "ログ", problems);

        Current = settings;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"アプリ設定を書き込めません: {ex.Message}");
        }

        return problems;
    }
}
