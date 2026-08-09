using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shionji.Application;
using Shionji.Infrastructure.Logging;

namespace Shionji.Infrastructure.Tests;

public class FileLoggerTests
{
    private static string ReadLog(TempDir dir) =>
        File.ReadAllText(Directory.GetFiles(dir.Path, "shionji-*.log").Single());

    /// <summary>書かれた各行を JSON として読み直す。1 行 1 レコードであることも同時に確かめている。</summary>
    private static List<JsonElement> ReadRecords(TempDir dir) =>
    [
        .. ReadLog(dir)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement),
    ];

    private static JsonElement ReadRecord(TempDir dir) => ReadRecords(dir).Single();

    private static string? Detail(JsonElement record, string key) =>
        record.TryGetProperty("details", out var details) && details.TryGetProperty(key, out var value)
            ? value.ToString()
            : null;

    [Test]
    public async Task 情報以上のログがファイルに書かれる()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);
        var logger = provider.CreateLogger("Shionji.Test");

        logger.LogInformation("接続しました {Name}", "api-db");
        logger.LogWarning("切断されました");

        var records = ReadRecords(dir);
        await Assert.That(records[0].GetProperty("level").GetString()).IsEqualTo("INF");
        await Assert.That(records[0].GetProperty("category").GetString()).IsEqualTo("Shionji.Test");
        await Assert.That(records[0].GetProperty("message").GetString()).IsEqualTo("接続しました api-db");
        await Assert.That(records[1].GetProperty("level").GetString()).IsEqualTo("WRN");
        await Assert.That(records[1].GetProperty("message").GetString()).IsEqualTo("切断されました");
    }

    [Test]
    public async Task 先頭にBOMを書かない()
    {
        // BOM が挟まると 1 行目を JSON として読めなくなる
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").LogInformation("先頭行");

        var bytes = await File.ReadAllBytesAsync(Directory.GetFiles(dir.Path, "shionji-*.log").Single());
        await Assert.That(bytes[0]).IsEqualTo((byte)'{');
    }

    [Test]
    public async Task 日本語はエスケープせずそのまま書く()
    {
        // 監査ではテキストエディタでそのまま読むため、\uXXXX へ落とさない
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").LogInformation("接続しました");

        await Assert.That(ReadLog(dir)).Contains("接続しました");
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

        // 例外を行に分けると 1 行 1 JSON が崩れるため、フィールドとして持たせる
        var record = ReadRecord(dir);
        await Assert.That(record.GetProperty("level").GetString()).IsEqualTo("ERR");
        await Assert.That(record.GetProperty("message").GetString()).IsEqualTo("処理に失敗しました");
        await Assert.That(record.GetProperty("exception").GetString())
            .Contains("InvalidOperationException: boom");
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
    public async Task 詳細フィールドはdetailsに入る()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").Audit(
            LogLevel.Information, "api-db: localhost:13306 で接続しました",
            ("destination", "db.example.internal:5432"),
            ("gateway", "EC2:i-0123456789abcdef0"),
            ("session", "s-0123456789abcdef0"));

        var record = ReadRecord(dir);
        await Assert.That(record.GetProperty("message").GetString())
            .IsEqualTo("api-db: localhost:13306 で接続しました");
        await Assert.That(Detail(record, "destination")).IsEqualTo("db.example.internal:5432");
        await Assert.That(Detail(record, "gateway")).IsEqualTo("EC2:i-0123456789abcdef0");
        await Assert.That(Detail(record, "session")).IsEqualTo("s-0123456789abcdef0");
    }

    [Test]
    public async Task 空白を含む値もそのまま読み出せる()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").Audit(
            LogLevel.Warning, "cache: 転送先の解決に失敗しました",
            ("cause", "条件に一致するリソースが 3 件あります"));

        await Assert.That(Detail(ReadRecord(dir), "cause"))
            .IsEqualTo("条件に一致するリソースが 3 件あります");
    }

    [Test]
    public async Task 数値の詳細はJSONの数値として書く()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").Audit(
            LogLevel.Information, "再接続を待っています",
            ("retryCount", 3), ("delaySeconds", 2.5), ("config", "cache"));

        var details = ReadRecord(dir).GetProperty("details");
        await Assert.That(details.GetProperty("retryCount").ValueKind).IsEqualTo(JsonValueKind.Number);
        await Assert.That(details.GetProperty("retryCount").GetInt32()).IsEqualTo(3);
        await Assert.That(details.GetProperty("delaySeconds").GetDouble()).IsEqualTo(2.5);
        await Assert.That(details.GetProperty("config").ValueKind).IsEqualTo(JsonValueKind.String);
    }

    [Test]
    public async Task 値が空の詳細は落とす()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").Audit(
            LogLevel.Information, "接続しました",
            ("session", null), ("gateway", ""), ("destination", "db:5432"));

        var details = ReadRecord(dir).GetProperty("details");
        await Assert.That(details.GetProperty("destination").GetString()).IsEqualTo("db:5432");
        await Assert.That(details.TryGetProperty("session", out _)).IsFalse();
        await Assert.That(details.TryGetProperty("gateway", out _)).IsFalse();
    }

    [Test]
    public async Task 詳細を持たないログにはdetailsを付けない()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").LogInformation("ふつうのログ");

        await Assert.That(ReadRecord(dir).TryGetProperty("details", out _)).IsFalse();
    }

    [Test]
    public async Task タイムスタンプはオフセット付きのISO8601()
    {
        using var dir = new TempDir();
        using var provider = new FileLoggerProvider(dir.Path);

        provider.CreateLogger("Shionji.Test").LogInformation("時刻の確認");

        var timestamp = ReadRecord(dir).GetProperty("timestamp").GetString()!;
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(
            timestamp, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2}$")).IsTrue();
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
