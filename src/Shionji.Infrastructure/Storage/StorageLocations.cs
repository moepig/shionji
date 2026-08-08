namespace Shionji.Infrastructure.Storage;

/// <summary>
/// 各ファイルの置き場所。
/// アプリ設定ファイルだけは固定 (置き場所の指定をそのファイル自身には書けないため)。
/// ログと接続先設定はアプリ設定で上書きでき、未指定なら既定フォルダを使う。
/// </summary>
public static class AppPaths
{
    public const string SettingsFileName = "appsettings.json";
    public const string ConfigsFileName = "configs.json";

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shionji");

    public static string DefaultLogDirectory => Path.Combine(DefaultDirectory, "logs");

    /// <summary>アプリ設定ファイル。移動できない。</summary>
    public static string SettingsFilePath => Path.Combine(DefaultDirectory, SettingsFileName);

    public static string ResolveLogDirectory(AppSettings settings) =>
        Blank(settings.LogDirectory) ? DefaultLogDirectory : settings.LogDirectory!;

    public static string ResolveConfigsDirectory(AppSettings settings) =>
        Blank(settings.ConfigsDirectory) ? DefaultDirectory : settings.ConfigsDirectory!;

    public static string ResolveConfigsFilePath(AppSettings settings) =>
        Path.Combine(ResolveConfigsDirectory(settings), ConfigsFileName);

    /// <summary>既定と同じ場所なら上書き指定を持たない (既定が変わったときに追従できる)。</summary>
    public static string? NormalizeDirectory(string directory, string defaultDirectory)
    {
        var trimmed = directory.Trim().TrimEnd('\\', '/');
        if (trimmed.Length == 0)
            return null;
        return string.Equals(trimmed, defaultDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}

/// <summary>保存先を変えたときの既存ファイルの引っ越し。</summary>
public static class StorageRelocation
{
    /// <summary>
    /// 移動先に既にファイルがある場合は上書きしない (利用者のデータを黙って捨てない)。
    /// 移動できなかった場合は理由を problems に積む。
    /// </summary>
    public static void MoveIfNeeded(string from, string to, string label, List<string> problems)
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

    /// <summary>保存先フォルダを用意する。作れない場合は理由を返す。</summary>
    public static void EnsureDirectory(string directory, string label, List<string> problems)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{label}: 保存先を作成できません ({ex.Message})。");
        }
    }
}
