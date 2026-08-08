using Shionji.Domain.Resolution;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

public class ConfigDetailViewModelTests
{
    [Test]
    public async Task 各項目が個別に取り出せる()
    {
        // 画面ではそれぞれにラベルを付けて並べるため、値が混ざっていないこと
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 13306));
        ui.Main.SelectedRow = ui.Main.Rows[0];

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.ProfileText).IsEqualTo("dev");
        await Assert.That(detail.RegionText).IsEqualTo("ap-northeast-1");
        await Assert.That(detail.GatewayText).IsEqualTo("EC2 踏み台 i-0123456789abcdef0");
        await Assert.That(detail.DestinationText).IsEqualTo("db.example.internal:5432");
        await Assert.That(detail.LocalPortText).IsEqualTo("localhost:13306");
        await Assert.That(detail.SessionText).IsEqualTo("未接続");
    }

    [Test]
    public async Task 自動割当は接続前後で表示が変わる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.QueryConfig());
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await Assert.That(detail.LocalPortText).IsEqualTo("自動割当 (接続時に決定)");
        await Assert.That(detail.LocalEndpoint).IsNull();

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(detail.LocalPortText).IsEqualTo("localhost:50000");
        await Assert.That(detail.LocalEndpoint).IsEqualTo("localhost:50000");
    }

    [Test]
    public async Task 候補とログの節は中身があるときだけ出す()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await Assert.That(detail.HasCandidates).IsFalse();
        await Assert.That(detail.HasLog).IsFalse();

        await row.ToggleConnectionCommand.ExecuteAsync(null);
        ui.App.Launcher.LastHandle.EmitLog("Port opened.");

        await Assert.That(detail.HasLog).IsTrue();
    }

    [Test]
    public async Task 確立中はローカルエンドポイントをコピーできる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 13306));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.LocalEndpoint).IsEqualTo("localhost:13306");

        detail.CopyLocalEndpointCommand.Execute(null);
        await Assert.That(ui.Clipboard.LastText).IsEqualTo("localhost:13306");
    }

    [Test]
    public async Task 接続と切断は状態に応じて片方だけ押せる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        // 未接続: 接続できる / 切断できない
        await Assert.That(detail.CanConnect).IsTrue();
        await Assert.That(detail.CanDisconnect).IsFalse();

        await detail.ConnectCommand.ExecuteAsync(null);

        // 確立: 接続できない / 切断できる
        await Assert.That(detail.CanConnect).IsFalse();
        await Assert.That(detail.CanDisconnect).IsTrue();

        await detail.DisconnectCommand.ExecuteAsync(null);

        await Assert.That(detail.CanConnect).IsTrue();
        await Assert.That(detail.CanDisconnect).IsFalse();
    }

    [Test]
    public async Task 失敗状態からは接続し直せる()
    {
        var ui = new UiHarness();
        ui.App.Probe.BusyPorts.Add(15432);
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await detail.ConnectCommand.ExecuteAsync(null);

        await Assert.That(detail.Status).IsEqualTo(StatusKind.Failed);
        await Assert.That(detail.CanConnect).IsTrue();
        await Assert.That(detail.CanDisconnect).IsFalse();
    }

    [Test]
    public async Task コピーすると完了表示が出る()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 13306));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await Assert.That(detail.IsCopyConfirmationVisible).IsFalse();

        detail.CopyLocalEndpointCommand.Execute(null);

        await Assert.That(detail.IsCopyConfirmationVisible).IsTrue();
        await Assert.That(detail.CopyConfirmationText).IsEqualTo("接続先をコピーしました");
    }

    [Test]
    public async Task セッションログをまとめてコピーできる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);
        ui.App.Launcher.LastHandle.EmitLog("first line");
        ui.App.Launcher.LastHandle.EmitLog("second line", isError: true);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        detail.CopyLogCommand.Execute(null);

        await Assert.That(ui.Clipboard.LastText!).Contains("first line");
        await Assert.That(ui.Clipboard.LastText!).Contains("[stderr] second line");
        await Assert.That(detail.CopyConfirmationText).IsEqualTo("ログ 2 行をコピーしました");
    }

    [Test]
    public async Task ログが無ければコピーしない()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        detail.CopyLogCommand.Execute(null);

        await Assert.That(ui.Clipboard.LastText).IsNull();
        await Assert.That(detail.IsCopyConfirmationVisible).IsFalse();
    }

    [Test]
    public async Task 転送先を特定できないと一覧と詳細で赤くする()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (_, _) => new ResolutionOutcome.Ambiguous(
            [TestData.Resource("a"), TestData.Resource("b"), TestData.Resource("c")]);
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        ui.Main.SelectedRow = ui.Main.Rows[0];

        await ui.App.Resolution.RefreshAsync(config);

        var row = ui.Main.Rows[0];
        await Assert.That(row.DestinationText).IsEqualTo("複数一致 (3 件)");
        await Assert.That(row.DestinationHasError).IsTrue();

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.DestinationHasError).IsTrue();
    }

    [Test]
    public async Task 特定できていれば赤くしない()
    {
        var ui = new UiHarness();
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        ui.Main.SelectedRow = ui.Main.Rows[0];

        await ui.App.Resolution.RefreshAsync(config);

        await Assert.That(ui.Main.Rows[0].DestinationHasError).IsFalse();
        await Assert.That(((ConfigDetailViewModel)ui.Main.DetailContent!).DestinationHasError).IsFalse();
    }

    [Test]
    public async Task 失敗時はフェーズ付きのエラーが表示される()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (_, _) => ResolutionOutcome.NotFound.Instance;
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.Status).IsEqualTo(StatusKind.Failed);
        await Assert.That(detail.ErrorText!).Contains("転送先の自動検索");
    }

    [Test]
    public async Task 複数一致の候補が一覧表示される()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (aws, query) => query is Shionji.Domain.Configuration.ElastiCacheQuery
            ? new ResolutionOutcome.Ambiguous(
            [
                TestData.Resource("redis-a", host: "a.cache.example"),
                TestData.Resource("redis-b", host: "b.cache.example"),
            ])
            : FakeCatalog.DefaultHandler(aws, query);
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        ui.Main.SelectedRow = ui.Main.Rows[0];

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await detail.RefreshResolutionCommand.ExecuteAsync(null);

        await Assert.That(detail.Candidates.Count).IsEqualTo(2);
        await Assert.That(detail.Candidates[0]).Contains("redis-a");
        await Assert.That(detail.ErrorText!).Contains("2 件");
    }

    [Test]
    public async Task 選択前のログも行切替後も失われない()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 15001));
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "cache", localPort: 15002));
        var apiRow = ui.Main.Rows.Single(r => r.Name == "api-db");
        var cacheRow = ui.Main.Rows.Single(r => r.Name == "cache");

        // どの行も選択していない状態でログが発生する
        await apiRow.ToggleConnectionCommand.ExecuteAsync(null);
        ui.App.Launcher.LastHandle.EmitLog("Port 15001 opened for sessionId x.");

        ui.Main.SelectedRow = apiRow;
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.LogLines.Count).IsEqualTo(1);

        // 別の行へ切り替えて戻ってもログが残る
        ui.Main.SelectedRow = cacheRow;
        ui.Main.SelectedRow = apiRow;
        detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.LogLines.Count).IsEqualTo(1);
    }

    [Test]
    public async Task セッションログが詳細に流れる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        ui.App.Launcher.LastHandle.EmitLog("Port 15432 opened for sessionId x.");

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.LogLines.Count).IsEqualTo(1);
    }
}
