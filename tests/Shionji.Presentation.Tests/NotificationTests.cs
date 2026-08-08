using Shionji.Domain.Resolution;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

/// <summary>
/// トースト通知は「使えていた接続が勝手に切れた」ときだけ出す。
/// 手動切断や最初から失敗した接続で鳴ると煩わしいだけになる。
/// </summary>
public class NotificationTests
{
    [Test]
    public async Task 手動で切断したときは通知しない()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(ui.Notifications.Notifications.Count).IsEqualTo(0);
    }

    [Test]
    public async Task 確立前に失敗したときは通知しない()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (_, _) => ResolutionOutcome.NotFound.Instance;
        await ui.App.Configs.SaveAsync(TestData.QueryConfig());
        var row = ui.Main.Rows[0];

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(ui.Main.Rows[0].Status).IsEqualTo(StatusKind.Failed);
        await Assert.That(ui.Notifications.Notifications.Count).IsEqualTo(0);
    }

    [Test]
    public async Task 自動再接続が無効なら切断は失敗として通知される()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "cache", autoReconnect: false));
        var row = ui.Main.Rows[0];
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        ui.App.Launcher.LastHandle.TriggerExit(TestData.Error());
        await Wait.UntilAsync(() => ui.Notifications.Notifications.Count > 0);

        var (name, message) = ui.Notifications.Notifications[0];
        await Assert.That(name).IsEqualTo("cache");
        await Assert.That(message).Contains("テスト用のエラー");
    }

    [Test]
    public async Task 再接続してもう一度切れたら再び通知される()
    {
        var ui = new UiHarness(new ImmediateScheduler());
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "cache", autoReconnect: true));
        var row = ui.Main.Rows[0];
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        ui.App.Launcher.Handles[0].TriggerExit(TestData.Error());
        await Wait.UntilAsync(() => ui.App.Launcher.LaunchCount == 2);
        await Wait.UntilAsync(() => ui.Main.Rows[0].Status == StatusKind.Connected);

        ui.App.Launcher.Handles[1].TriggerExit(TestData.Error());
        await Wait.UntilAsync(() => ui.Notifications.Notifications.Count == 2);

        await Assert.That(ui.Notifications.Notifications.Count).IsEqualTo(2);
    }
}
