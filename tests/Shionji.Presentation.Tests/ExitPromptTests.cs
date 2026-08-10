using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

/// <summary>終了の確認。要否と文面だけを持ち、ダイアログそのものは各 UI にある。</summary>
public class ExitPromptTests
{
    [Test]
    public async Task 確認しない設定なら何も出さない()
    {
        var prompt = ExitPrompt.For(confirmOnExit: false, connectedCount: 3);

        await Assert.That(prompt).IsNull();
    }

    [Test]
    public async Task 接続中があれば件数を示す()
    {
        var prompt = ExitPrompt.For(confirmOnExit: true, connectedCount: 2);

        await Assert.That(prompt!.Title).IsEqualTo("Shionji を終了しますか?");
        await Assert.That(prompt.Message).IsEqualTo("接続中の 2 件を切断して終了します。");
    }

    [Test]
    public async Task 接続中が無ければ切断の話はしない()
    {
        // 切れるものが無いのに「切断します」と出ると、何が起きるのか読めない
        var prompt = ExitPrompt.For(confirmOnExit: true, connectedCount: 0);

        await Assert.That(prompt!.Message).DoesNotContain("切断");
        await Assert.That(prompt.Message).Contains("終了");
    }

    [Test]
    public async Task 接続中の件数は接続した数に追従する()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 15001));
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "cache", localPort: 15002));

        await Assert.That(ui.Main.ConnectedCount).IsEqualTo(0);

        await ui.Main.Rows.Single(row => row.Name == "api-db").ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(ui.Main.ConnectedCount).IsEqualTo(1);

        await ui.Main.ConnectAllCommand.ExecuteAsync(null);

        await Assert.That(ui.Main.ConnectedCount).IsEqualTo(2);
    }

    [Test]
    public async Task 切断した分は件数から外れる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(ui.Main.ConnectedCount).IsEqualTo(0);
    }
}
