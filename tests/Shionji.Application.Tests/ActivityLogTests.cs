using Microsoft.Extensions.Logging;
using Shionji.TestSupport;

namespace Shionji.Application.Tests;

public class ActivityLogTests
{
    private static ActivityLog NewLog() => new(new FakeClock());

    [Test]
    public async Task 投稿した内容が最新として取れる()
    {
        var log = NewLog();

        log.Post(ActivitySeverity.Info, "つないでいます");
        log.Post(ActivitySeverity.Error, "失敗しました");

        await Assert.That(log.Latest!.Message).IsEqualTo("失敗しました");
        await Assert.That(log.Latest.Severity).IsEqualTo(ActivitySeverity.Error);
        await Assert.That(log.Recent.Count).IsEqualTo(2);
    }

    [Test]
    public async Task 何も無ければ最新はnull()
    {
        await Assert.That(NewLog().Latest).IsNull();
    }

    [Test]
    public async Task 末尾200件に丸められる()
    {
        var log = NewLog();

        for (var i = 0; i < 250; i++)
            log.Post(ActivitySeverity.Info, $"line {i}");

        var recent = log.Recent;
        await Assert.That(recent.Count).IsEqualTo(200);
        await Assert.That(recent[0].Message).IsEqualTo("line 50");
        await Assert.That(recent[^1].Message).IsEqualTo("line 249");
    }

    [Test]
    public async Task 投稿のたびにイベントが出る()
    {
        var log = NewLog();
        var received = new List<ActivityEntry>();
        log.Posted += (_, entry) => received.Add(entry);

        log.Post(ActivitySeverity.Warning, "注意");

        await Assert.That(received.Single().Severity).IsEqualTo(ActivitySeverity.Warning);
    }

    [Test]
    [Arguments(LogLevel.Information, ActivitySeverity.Info)]
    [Arguments(LogLevel.Warning, ActivitySeverity.Warning)]
    [Arguments(LogLevel.Error, ActivitySeverity.Error)]
    [Arguments(LogLevel.Critical, ActivitySeverity.Error)]
    public async Task ログレベルが重大度に対応する(LogLevel level, ActivitySeverity expected)
    {
        var log = NewLog();
        using var factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new ActivityLogProvider(log));
        });

        factory.CreateLogger("test").Log(level, "こんにちは {Name}", "世界");

        await Assert.That(log.Latest!.Severity).IsEqualTo(expected);
        await Assert.That(log.Latest.Message).IsEqualTo("こんにちは 世界");
    }

    [Test]
    public async Task 情報未満は画面に出さない()
    {
        var log = NewLog();
        using var factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new ActivityLogProvider(log));
        });

        var logger = factory.CreateLogger("test");
        logger.LogDebug("詳細");
        logger.LogTrace("さらに詳細");

        await Assert.That(log.Recent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task 接続の流れがそのまま履歴に残る()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig(name: "api-db", localPort: 13306);

        await harness.Supervisor.StartAsync(config);

        var messages = harness.Activity.Recent.Select(e => e.Message).ToList();
        await Assert.That(messages.Any(m => m.StartsWith("[設定名: api-db] リソースを自動検索しています…"))).IsTrue();
        await Assert.That(messages.Any(m => m.StartsWith("[設定名: api-db] セッションを開始しています…"))).IsTrue();
        await Assert.That(messages.Any(m => m.StartsWith("[設定名: api-db] localhost:13306 で接続しました"))).IsTrue();
    }

    [Test]
    public async Task 失敗は重大度Errorで残る()
    {
        var harness = new Harness();
        harness.Probe.BusyPorts.Add(15432);

        await harness.Supervisor.StartAsync(TestData.StaticConfig(name: "api-db"));

        var failure = harness.Activity.Recent.Last();
        await Assert.That(failure.Severity).IsEqualTo(ActivitySeverity.Error);
        await Assert.That(failure.Message).IsEqualTo("[設定名: api-db] 失敗: ローカルポート 15432 は使用中です。");
    }

    [Test]
    public async Task 設定の保存と削除も履歴に残る()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig(name: "api-db");

        await harness.Configs.SaveAsync(config);
        await harness.Configs.DeleteAsync(config.Id);

        var messages = harness.Activity.Recent.Select(e => e.Message).ToList();
        await Assert.That(messages).Contains("[設定名: api-db] 設定を保存しました");
        await Assert.That(messages).Contains("[設定名: api-db] 設定を削除しました");
    }
}
