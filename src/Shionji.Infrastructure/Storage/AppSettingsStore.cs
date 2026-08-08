using System.Text.Json;

namespace Shionji.Infrastructure.Storage;

/// <summary>アプリ全体の設定 (転送設定とは別)。</summary>
public sealed class AppSettings
{
    /// <summary>session-manager-plugin.exe の上書きパス。null なら自動探索。</summary>
    public string? PluginPath { get; set; }

    /// <summary>AWS API のエンドポイント上書き (VPC エンドポイントなど)。null なら通常のリージョン解決。</summary>
    public string? AwsEndpointOverride { get; set; }

    /// <summary>ウィンドウを閉じたときにタスクトレイへ格納する。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>ログファイルの保持日数。監査要件に応じて延ばす。</summary>
    public int LogRetentionDays { get; set; } = 30;

    /// <summary>カラーテーマ。System / Light / Dark。</summary>
    public string Theme { get; set; } = "System";
}

/// <summary>%APPDATA%/Shionji/appsettings.json の読み書き。</summary>
public sealed class AppSettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(StorageLocations.DefaultDirectory, "appsettings.json");

    public AppSettings Current { get; private set; } = new();

    /// <summary>
    /// 読めない場合は既定値で続行する。設定ファイルの不備でアプリが起動できなくなる方が害が大きい。
    /// </summary>
    public AppSettings Load()
    {
        if (File.Exists(filePath))
        {
            try
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), JsonOptions)
                    ?? new AppSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Current = new AppSettings();
            }
        }

        return Current;
    }

    public void Save(AppSettings settings)
    {
        Current = settings;
        var directory = Path.GetDirectoryName(filePath);
        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
