using Microsoft.Extensions.Logging;
using Shionji.Infrastructure.Logging;

namespace Shionji.Infrastructure.Tests;

public class FileLoggerTests
{
    private static string ReadLog(TempDir dir) =>
        File.ReadAllText(Directory.GetFiles(dir.Path, "shionji-*.log").Single());

    [Test]
    public async Task 情報以上のログがファイルに書かれる()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);
        var logger = provider.CreateLogger("Shionji.Test");

        logger.LogInformation("接続しました {Name}", "api-db");
        logger.LogWarning("切断されました");

        var content = ReadLog(dir);
        await Assert.That(content).Contains("[INF] Shionji.Test: 接続しました api-db");
        await Assert.That(content).Contains("[WRN] Shionji.Test: 切断されました");
    }

    [Test]
    public async Task デバッグ以下は書かれない()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);
        var logger = provider.CreateLogger("Shionji.Test");

        logger.LogDebug("詳細");
        logger.LogTrace("さらに詳細");

        await Assert.That(Directory.GetFiles(dir.Path, "shionji-*.log").Length).IsEqualTo(0);
    }

    [Test]
    public async Task 例外はスタックトレース付きで残る()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);
        var logger = provider.CreateLogger("Shionji.Test");

        logger.LogError(new InvalidOperationException("boom"), "処理に失敗しました");

        var content = ReadLog(dir);
        await Assert.That(content).Contains("[ERR]");
        await Assert.That(content).Contains("処理に失敗しました");
        await Assert.That(content).Contains("InvalidOperationException: boom");
    }

    [Test]
    public async Task 古いログは起動時に削除され最近のものは残る()
    {
        using var dir = new TempDir();
        var old = Path.Combine(dir.Path, "shionji-20200101.log");
        var recent = Path.Combine(dir.Path, "shionji-20991231.log");
        await File.WriteAllTextAsync(old, "old");
        await File.WriteAllTextAsync(recent, "recent");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
        File.SetLastWriteTime(recent, DateTime.Now.AddDays(-1));

        using var provider = new FileLoggerProvider(dir.Path);

        await Assert.That(File.Exists(old)).IsFalse();
        await Assert.That(File.Exists(recent)).IsTrue();
    }

    [Test]
    public async Task ディレクトリが無ければ作る()
    {
        using var dir = new TempDir();
        var nested = Path.Combine(dir.Path, "logs", "nested");

        using var provider = new FileLoggerProvider(nested);
        provider.CreateLogger("x").LogInformation("hello");

        await Assert.That(Directory.Exists(nested)).IsTrue();
    }
}
