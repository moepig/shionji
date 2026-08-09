using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;

namespace Shionji.Infrastructure.Tunnel;

/// <summary>
/// session-manager-plugin.exe の探索。
/// アプリ設定の上書きパス → 既定インストールパス → PATH の順で探す。
/// </summary>
/// <param name="configuredPathProvider">アプリ設定の上書きパス。null と空文字は未指定として扱う</param>
/// <param name="programFilesProvider">既定インストールパスの基点。省略時は実行環境の Program Files</param>
public sealed class SessionManagerPluginLocator(
    Func<string?>? configuredPathProvider = null,
    Func<string>? programFilesProvider = null)
{
    public const string InstallGuideUrl =
        "https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html";

    private const string ExeName = "session-manager-plugin.exe";

    public Result<string, ErrorDetail> Locate()
    {
        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate))
                return Result<string, ErrorDetail>.Success(candidate);
        }

        return Result<string, ErrorDetail>.Failure(new ErrorDetail(
            FailurePhase.Plugin,
            "PluginNotFound",
            $"session-manager-plugin が見つかりません。{InstallGuideUrl} からインストールするか、" +
            "アプリ設定でパスを指定してください。"));
    }

    private IEnumerable<string> Candidates()
    {
        if (configuredPathProvider?.Invoke() is { Length: > 0 } configured)
            yield return configured;

        var programFiles = programFilesProvider is null
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            : programFilesProvider();
        if (programFiles.Length > 0)
            yield return Path.Combine(programFiles, "Amazon", "SessionManagerPlugin", "bin", ExeName);

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(dir.Trim(), ExeName);
    }
}
