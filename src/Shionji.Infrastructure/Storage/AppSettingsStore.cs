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
}

/// <summary>%APPDATA%/Shionji/appsettings.json の読み書き。</summary>
public sealed class AppSettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shionji", "appsettings.json");

    public AppSettings Current { get; private set; } = new();

    public AppSettings Load()
    {
        if (File.Exists(filePath))
        {
            try
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), JsonOptions) ?? new AppSettings();
            }
            catch (JsonException)
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
