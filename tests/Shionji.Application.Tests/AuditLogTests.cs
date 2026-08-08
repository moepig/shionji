using Shionji.TestSupport;

namespace Shionji.Application.Tests;

/// <summary>
/// テキストログに監査用の詳細が残ること、そして画面向けの要約が簡潔なままであることを固定する。
/// 「いつ・誰が・どの資格情報で・どの踏み台を経由して・どこへ繋いだか」が辿れなければ意味がない。
/// </summary>
public class AuditLogTests
{
    [Test]
    public async Task 接続確立の記録に経路と転送先と資格情報が残る()
    {
        var harness = new Harness();

        await harness.Supervisor.StartAsync(TestData.StaticConfig(name: "api-db", localPort: 13306));

        var established = harness.WrittenWith("で接続しました");
        await Assert.That(established.Detail("転送先")).IsEqualTo("db.example.internal:5432");
        await Assert.That(established.Detail("経路")).IsEqualTo("EC2:i-0123456789abcdef0");
        await Assert.That(established.Detail("SSMターゲット")).IsEqualTo("i-0123456789abcdef0");
        await Assert.That(established.Detail("文書")).IsEqualTo("AWS-StartPortForwardingSessionToRemoteHost");
        await Assert.That(established.Detail("プロファイル")).IsEqualTo("dev@ap-northeast-1");
        await Assert.That(established.Detail("ローカル")).IsEqualTo("localhost:13306");
        // CloudTrail / ssm:DescribeSessions と突き合わせるための鍵
        await Assert.That(established.Detail("セッション")).IsEqualTo("s-test0123456789");
    }

    [Test]
    public async Task 画面向けの要約は簡潔なまま()
    {
        var harness = new Harness();

        await harness.Supervisor.StartAsync(TestData.StaticConfig(name: "api-db", localPort: 13306));

        // ステータスバーに出るのは要約だけで、詳細は混ざらない
        var summaries = harness.Activity.Recent.Select(e => e.Message).ToList();
        await Assert.That(summaries).Contains("api-db: localhost:13306 で接続しました");
        await Assert.That(summaries).Contains("api-db: リソースを解決しています…");
        await Assert.That(summaries.Any(s => s.Contains('='))).IsFalse();
        await Assert.That(summaries.All(s => s.Length <= 60)).IsTrue();
    }

    [Test]
    public async Task Direct転送も経路の別なく記録される()
    {
        var harness = new Harness();

        await harness.Supervisor.StartAsync(TestData.DirectEc2Config(name: "batch", localPort: 12222));

        var established = harness.WrittenWith("で接続しました");
        await Assert.That(established.Detail("経路")).IsEqualTo("直接");
        await Assert.That(established.Detail("文書")).IsEqualTo("AWS-StartPortForwardingSession");
        await Assert.That(established.Detail("転送先")).IsEqualTo("i-0feedfacefeedface:22");
    }

    [Test]
    public async Task 検索条件がどの実リソースに解決されたか残る()
    {
        var harness = new Harness();

        await harness.Supervisor.StartAsync(TestData.QueryConfig());

        var resolved = harness.WrittenWith("リソースを解決しました");
        await Assert.That(resolved.Detail("転送先")).IsEqualTo("cache-1");
        await Assert.That(resolved.Detail("転送先ID")).IsEqualTo("cache-1");
        await Assert.That(resolved.Detail("転送先エンドポイント")).IsEqualTo("redis.prod.example.com:6379");
        await Assert.That(resolved.Detail("踏み台SSM")).IsEqualTo("i-0feedfacefeedface");
    }

    [Test]
    public async Task 一覧更新の解決結果にもリソース識別子が残る()
    {
        var harness = new Harness();

        await harness.Resolution.RefreshAsync(TestData.QueryConfig());

        var resolved = harness.WrittenWith("転送先を cache-1 に解決しました");
        await Assert.That(resolved.Detail("リソースID")).IsEqualTo("cache-1");
        await Assert.That(resolved.Detail("エンドポイント")).IsEqualTo("redis.prod.example.com");
        await Assert.That(resolved.Detail("プロファイル")).IsEqualTo("dev@ap-northeast-1");
    }

    [Test]
    public async Task 同じ試行のログは共通の相関IDで辿れる()
    {
        var harness = new Harness();

        await harness.Supervisor.StartAsync(TestData.StaticConfig(name: "api-db"));

        var ids = harness.Written
            .Select(w => w.Detail("試行"))
            .Where(id => id is { Length: > 0 })
            .Distinct()
            .ToList();

        await Assert.That(ids.Count).IsEqualTo(1);
    }

    [Test]
    public async Task 再接続は別の相関IDになる()
    {
        var harness = new Harness(new ImmediateScheduler());
        await harness.Supervisor.StartAsync(TestData.StaticConfig(name: "cache", autoReconnect: true));

        harness.Launcher.LastHandle.TriggerExit(TestData.Error());
        await Wait.UntilAsync(() => harness.Launcher.LaunchCount == 2);

        var ids = harness.Written
            .Select(w => w.Detail("試行"))
            .Where(id => id is { Length: > 0 })
            .Distinct()
            .ToList();

        await Assert.That(ids.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(StopReason.UserRequest, "利用者操作")]
    [Arguments(StopReason.ConfigChanged, "設定変更")]
    [Arguments(StopReason.ApplicationExit, "アプリ終了")]
    public async Task 切断は理由と接続時間つきで記録される(StopReason reason, string expected)
    {
        var harness = new Harness();
        var config = TestData.StaticConfig(name: "api-db");
        await harness.Supervisor.StartAsync(config);

        await harness.Supervisor.StopAsync(config.Id, reason);

        var stop = harness.WrittenWith("切断します");
        await Assert.That(stop.Detail("理由")).IsEqualTo(expected);
        await Assert.That(stop.Detail("セッション")).IsEqualTo("s-test0123456789");
        await Assert.That(stop.Detail("接続秒")).IsNotNull();
    }

    [Test]
    public async Task 設定変更に伴う切断はそう記録される()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig(name: "api-db");
        await harness.Supervisor.StartAsync(config);

        await harness.Configs.SaveAsync(config);

        await Assert.That(harness.WrittenWith("切断します").Detail("理由")).IsEqualTo("設定変更");
    }

    [Test]
    public async Task 予期せぬ終了は原因とセッションと接続時間を残す()
    {
        var harness = new Harness();
        await harness.Supervisor.StartAsync(TestData.StaticConfig(name: "cache"));

        harness.Launcher.LastHandle.TriggerExit(TestData.Error(Domain.Resolution.FailurePhase.Plugin));
        await Wait.UntilAsync(() => harness.Written.Any(w => w.Summary.Contains("接続が切れました")));

        var exited = harness.WrittenWith("接続が切れました");
        await Assert.That(exited.Detail("セッション")).IsEqualTo("s-test0123456789");
        await Assert.That(exited.Detail("フェーズ")).IsEqualTo("Plugin");
        await Assert.That(exited.Detail("コード")).IsEqualTo("TestError");
        await Assert.That(exited.Detail("接続秒")).IsNotNull();
    }

    [Test]
    public async Task 失敗はフェーズとコードを伴って残る()
    {
        var harness = new Harness();
        harness.Probe.BusyPorts.Add(15432);

        await harness.Supervisor.StartAsync(TestData.StaticConfig(name: "api-db"));

        var failed = harness.WrittenWith("失敗:");
        await Assert.That(failed.Detail("フェーズ")).IsEqualTo("StartSession");
        await Assert.That(failed.Detail("コード")).IsEqualTo("LocalPortInUse");
        await Assert.That(failed.Detail("プロファイル")).IsEqualTo("dev@ap-northeast-1");
    }

    [Test]
    public async Task 複数一致は候補まで残る()
    {
        var harness = new Harness();
        harness.Catalog.Handler = (_, _) => new Domain.Resolution.ResolutionOutcome.Ambiguous(
            [TestData.Resource("redis-a"), TestData.Resource("redis-b")]);

        await harness.Resolution.RefreshAsync(TestData.QueryConfig());

        var ambiguous = harness.WrittenWith("件一致しました");
        await Assert.That(ambiguous.Detail("候補数")).IsEqualTo("2");
        await Assert.That(ambiguous.Detail("候補")).Contains("redis-a[redis-a]");
        await Assert.That(ambiguous.Detail("候補")).Contains("redis-b[redis-b]");
    }

    [Test]
    public async Task 設定の保存と削除は設定IDまで残る()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig(name: "api-db");

        await harness.Configs.SaveAsync(config);
        await Assert.That(harness.WrittenWith("を保存しました").Detail("設定ID"))
            .IsEqualTo(config.Id.Value.ToString());

        await harness.Configs.DeleteAsync(config.Id);
        await Assert.That(harness.WrittenWith("を削除しました").Detail("操作")).IsEqualTo("削除");
    }

    [Test]
    public async Task 起動時に実行環境が記録される()
    {
        var harness = new Harness();
        var startup = new StartupService(
            harness.Configs, harness.Resolution, harness.Supervisor, harness.LoggerFor<StartupService>());

        await startup.RunAsync();

        var start = harness.WrittenWith("Shionji を起動しました");
        await Assert.That(start.Detail("利用者")).IsNotNull();
        await Assert.That(start.Detail("端末")).IsEqualTo(Environment.MachineName);
        await Assert.That(start.Detail("プロセス")).IsEqualTo(Environment.ProcessId.ToString());
    }
}
