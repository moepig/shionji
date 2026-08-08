using Shionji.Application;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

public class StatusBarTests
{
    [Test]
    public async Task 最新の動作がステータスに出る()
    {
        var ui = new UiHarness();

        ui.App.Activity.Post(ActivitySeverity.Warning, "切断されました");

        await Assert.That(ui.Main.StatusText).IsEqualTo("切断されました");
        await Assert.That(ui.Main.StatusSeverity).IsEqualTo(ActivitySeverity.Warning);
        await Assert.That(ui.Main.StatusTime).IsNotEmpty();
    }

    [Test]
    public async Task 履歴は新しい順に並ぶ()
    {
        var ui = new UiHarness();

        ui.App.Activity.Post(ActivitySeverity.Info, "1 番目");
        ui.App.Activity.Post(ActivitySeverity.Info, "2 番目");

        await Assert.That(ui.Main.Activities.Select(a => a.Message)).IsEquivalentTo(["2 番目", "1 番目"]);
    }

    [Test]
    public async Task 生成前の履歴も取り込まれる()
    {
        // 起動処理のログは MainViewModel の生成より先に出ることがある
        var harness = new Harness();
        harness.Activity.Post(ActivitySeverity.Info, "起動前の出来事");

        var ui = new UiHarness(harness);

        await Assert.That(ui.Main.StatusText).IsEqualTo("起動前の出来事");
        await Assert.That(ui.Main.Activities.Count).IsEqualTo(1);
    }

    [Test]
    public async Task 履歴は200件で打ち切られる()
    {
        var ui = new UiHarness();

        for (var i = 0; i < 250; i++)
            ui.App.Activity.Post(ActivitySeverity.Info, $"line {i}");

        await Assert.That(ui.Main.Activities.Count).IsEqualTo(200);
        await Assert.That(ui.Main.Activities[0].Message).IsEqualTo("line 249");
    }

    [Test]
    public async Task 接続操作の状況がステータスに反映される()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 13306));

        await ui.Main.Rows[0].ToggleConnectionCommand.ExecuteAsync(null);

        // ステータスバーには要約だけが出る (詳細はテキストログ側)
        await Assert.That(ui.Main.StatusText).IsEqualTo("[設定名: api-db] localhost:13306 で接続しました");
        await Assert.That(ui.Main.StatusSeverity).IsEqualTo(ActivitySeverity.Info);
    }

    [Test]
    public async Task ログの場所を開ける()
    {
        var ui = new UiHarness();

        await Assert.That(ui.Main.LogDirectory).IsEqualTo(@"C:\fake\logs");

        ui.Main.OpenLogLocationCommand.Execute(null);

        await Assert.That(ui.FileLocation.OpenCount).IsEqualTo(1);
    }
}
