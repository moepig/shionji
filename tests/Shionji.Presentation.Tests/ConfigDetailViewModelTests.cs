using Shionji.Domain.Resolution;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

public class ConfigDetailViewModelTests
{
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
        await Assert.That(detail.ErrorText!).Contains("転送先の解決");
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
