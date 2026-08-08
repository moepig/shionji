using Microsoft.Extensions.Logging;
using Shionji.Application;
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
    public async Task 詳細フィールドはkey値として展開される()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").Audit(
            LogLevel.Information, "api-db: localhost:13306 で接続しました",
            ("転送先", "db.example.internal:5432"),
            ("経路", "EC2:i-0123456789abcdef0"),
            ("セッション", "s-0123456789abcdef0"));

        var content = ReadLog(dir);
        await Assert.That(content).Contains("api-db: localhost:13306 で接続しました |");
        await Assert.That(content).Contains("転送先=db.example.internal:5432");
        await Assert.That(content).Contains("経路=EC2:i-0123456789abcdef0");
        await Assert.That(content).Contains("セッション=s-0123456789abcdef0");
    }

    [Test]
    public async Task 空白を含む値は引用符で囲む()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").Audit(
            LogLevel.Warning, "cache: 転送先の解決に失敗しました",
            ("原因", "条件に一致するリソースが 3 件あります"));

        await Assert.That(ReadLog(dir)).Contains("原因=\"条件に一致するリソースが 3 件あります\"");
    }

    [Test]
    public async Task 値が空の詳細は落とす()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").Audit(
            LogLevel.Information, "接続しました",
            ("セッション", null), ("経路", ""), ("転送先", "db:5432"));

        var content = ReadLog(dir);
        await Assert.That(content).Contains("転送先=db:5432");
        await Assert.That(content.Contains("セッション=")).IsFalse();
        await Assert.That(content.Contains("経路=")).IsFalse();
    }

    [Test]
    public async Task 詳細を持たないログは区切りを付けない()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").LogInformation("ふつうのログ");

        await Assert.That(ReadLog(dir).Contains(" |")).IsFalse();
    }

    [Test]
    public async Task タイムスタンプはオフセット付きのISO8601()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").LogInformation("時刻の確認");

        var line = ReadLog(dir).Split(Environment.NewLine)[0];
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(
            line, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2} ")).IsTrue();
    }

    [Test]
    public async Task 保持日数は指定できる()
    {
        using var dir = new TempDir();
        var old = Path.Combine(dir.Path, "shionji-20200101.log");
        await File.WriteAllTextAsync(old, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-20));

        // 既定 (30 日) では残るが、7 日指定では消える
        using (var keep = new FileLoggerProvider(dir.Path))
            await Assert.That(File.Exists(old)).IsTrue();

        using var purge = new FileLoggerProvider(dir.Path, retentionDays: 7);
        await Assert.That(File.Exists(old)).IsFalse();
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
