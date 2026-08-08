using Shionji.Domain.Tunneling;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

public class MainViewModelTests
{
    [Test]
    public async Task 設定の保存で行が追加される()
    {
        var ui = new UiHarness();

        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));

        await Assert.That(ui.Main.Rows.Count).IsEqualTo(1);
        await Assert.That(ui.Main.Rows[0].Name).IsEqualTo("api-db");
        await Assert.That(ui.Main.Rows[0].Status).IsEqualTo(StatusKind.NotConnected);
    }

    [Test]
    public async Task フィルタで設定名を絞り込める()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 15001));
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "cache", localPort: 15002));
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-batch", localPort: 15003));

        ui.Main.FilterText = "api";

        await Assert.That(ui.Main.Rows.Select(r => r.Name)).IsEquivalentTo(["api-batch", "api-db"]);

        ui.Main.FilterText = string.Empty;
        await Assert.That(ui.Main.Rows.Count).IsEqualTo(3);
    }

    [Test]
    public async Task トグルで接続され行の状態が緑になる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(row.Status).IsEqualTo(StatusKind.Connected);
        await Assert.That(row.IsConnected).IsTrue();
        await Assert.That(row.Summary).IsEqualTo(":15432 → db.example.internal:5432");
    }

    [Test]
    public async Task 接続中にトグルすると切断される()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(row.Status).IsEqualTo(StatusKind.NotConnected);
        await Assert.That(ui.App.Launcher.LastHandle.Stopped).IsTrue();
    }

    [Test]
    public async Task 確立後の予期せぬ切断は通知される()
    {
        var ui = new UiHarness(new BlockingScheduler());
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "cache", autoReconnect: true));
        var row = ui.Main.Rows[0];
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        ui.App.Launcher.LastHandle.TriggerExit(TestData.Error());
        await Wait.UntilAsync(() => ui.Notifications.Notifications.Count > 0);

        var (name, message) = ui.Notifications.Notifications[0];
        await Assert.That(name).IsEqualTo("cache");
        await Assert.That(message).Contains("再接続");
    }

    [Test]
    public async Task 行の選択で詳細ペインが開き削除で消える()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];

        ui.Main.SelectedRow = row;
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.Name).IsEqualTo("api-db");

        await detail.DeleteCommand.ExecuteAsync(null);
        await Assert.That(ui.Main.Rows.Count).IsEqualTo(0);
        await Assert.That(ui.Main.DetailContent).IsNull();
    }

    [Test]
    public async Task 別の設定を保存しても行インスタンスと選択が維持される()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 15001));
        var apiRow = ui.Main.Rows.Single(r => r.Name == "api-db");
        ui.Main.SelectedRow = apiRow;

        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "cache", localPort: 15002));

        // 行 VM は再利用され、選択も外れない
        await Assert.That(ReferenceEquals(ui.Main.Rows.Single(r => r.Name == "api-db"), apiRow)).IsTrue();
        await Assert.That(ui.Main.SelectedRow).IsEqualTo(apiRow);
        await Assert.That(ui.Main.Rows.Select(r => r.Name)).IsEquivalentTo(["api-db", "cache"]);
    }

    [Test]
    public async Task 追加ボタンで新規エディタが開く()
    {
        var ui = new UiHarness();

        ui.Main.AddConfigCommand.Execute(null);

        var editor = (ConfigEditorViewModel)ui.Main.DetailContent!;
        await Assert.That(editor.IsNew).IsTrue();
    }
}
