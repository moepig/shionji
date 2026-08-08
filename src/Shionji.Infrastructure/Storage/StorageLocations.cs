using System.Text.Json;

namespace Shionji.Infrastructure.Storage;

/// <summary>
/// 各ファイルの保存先。null なら既定の %APPDATA%\Shionji 配下。
/// </summary>
public sealed class StorageLocations
{
    /// <summary>appsettings.json を置くフォルダ。</summary>
    public string? SettingsDirectory { get; set; }

    /// <summary>configs.json を置くフォルダ。</summary>
    public string? ConfigsDirectory { get; set; }

    /// <summary>ログファイルを置くフォルダ。</summary>
    public string? LogDirectory { get; set; }

    public const string SettingsFileName = "appsettings.json";
    public const string ConfigsFileName = "configs.json";

    /// <summary>上書きが無いときの基準フォルダ。</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shionji");

    public static string DefaultLogDirectory => Path.Combine(DefaultDirectory, "logs");

    public string ResolvedSettingsDirectory => Blank(SettingsDirectory) ? DefaultDirectory : SettingsDirectory!;
    public string ResolvedConfigsDirectory => Blank(ConfigsDirectory) ? DefaultDirectory : ConfigsDirectory!;
    public string ResolvedLogDirectory => Blank(LogDirectory) ? DefaultLogDirectory : LogDirectory!;

    public string SettingsFilePath => Path.Combine(ResolvedSettingsDirectory, SettingsFileName);
    public string ConfigsFilePath => Path.Combine(ResolvedConfigsDirectory, ConfigsFileName);

    public StorageLocations Clone() => new()
    {
        SettingsDirectory = SettingsDirectory,
        ConfigsDirectory = ConfigsDirectory,
        LogDirectory = LogDirectory,
    };

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}

/// <summary>
/// 保存先の指定そのものを保持するブートストラップファイルの読み書き。
/// 「設定ファイルの置き場所」を設定ファイルに書くことはできないので、
/// この 1 ファイルだけは常に既定フォルダ (%APPDATA%\Shionji\locations.json) に置く。
/// </summary>
public sealed class StorageLocationsStore(string? bootstrapPath = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string BootstrapPath { get; } =
        bootstrapPath ?? Path.Combine(StorageLocations.DefaultDirectory, "locations.json");

    public StorageLocations Current { get; private set; } = new();

    /// <summary>読めない場合は既定値で続行する (起動できなくなる方が害が大きい)。</summary>
    public StorageLocations Load()
    {
        if (File.Exists(BootstrapPath))
        {
            try
            {
                Current = JsonSerializer.Deserialize<StorageLocations>(File.ReadAllText(BootstrapPath), JsonOptions)
                    ?? new StorageLocations();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Current = new StorageLocations();
            }
        }

        return Current;
    }

    /// <summary>
    /// 新しい保存先を書き込み、既存ファイルを移す。
    /// 移動に失敗した項目は理由を返す (指定自体は保存され、次回起動時に新しい場所を見る)。
    /// </summary>
    public IReadOnlyList<string> Save(StorageLocations locations)
    {
        List<string> problems = [];

        MoveIfNeeded(Current.SettingsFilePath, locations.SettingsFilePath, "アプリ設定ファイル", problems);
        MoveIfNeeded(Current.ConfigsFilePath, locations.ConfigsFilePath, "接続先設定ファイル", problems);

        try
        {
            Directory.CreateDirectory(locations.ResolvedLogDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"ログの保存先を作成できません: {ex.Message}");
        }

        Current = locations;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootstrapPath)!);
            File.WriteAllText(BootstrapPath, JsonSerializer.Serialize(locations, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"保存先の指定を書き込めません: {ex.Message}");
        }

        return problems;
    }

    /// <summary>移動先に既にファイルがある場合は上書きしない (利用者のデータを黙って捨てない)。</summary>
    private static void MoveIfNeeded(string from, string to, string label, List<string> problems)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || !File.Exists(from))
            return;

        if (File.Exists(to))
        {
            problems.Add($"{label}: 移動先に同名のファイルがあるため移動しませんでした ({to})。");
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{label}: 移動できませんでした ({ex.Message})。");
        }
    }
}
