using Shionji.Domain.Tunneling;

namespace Shionji.Application.Tests;

public class ConfigServiceTests
{
    [Test]
    public async Task ロードで一覧が読み込まれる()
    {
        var repo = new InMemoryRepository(TestData.StaticConfig(name: "a"), TestData.StaticConfig(name: "b"));
        var harness = new Harness(repository: repo);

        await harness.Configs.LoadAsync();

        await Assert.That(harness.Configs.Configs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task 接続中の設定の保存は切断してから行う()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();
        await harness.Supervisor.StartAsync(config);

        await harness.Configs.SaveAsync(config);

        await Assert.That(harness.Launcher.LastHandle.Stopped).IsTrue();
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();
        await Assert.That(harness.Repository.SaveCount).IsEqualTo(1);
        await Assert.That(harness.Configs.Find(config.Id)).IsEqualTo(config);
    }

    [Test]
    public async Task 削除は切断し設定と解決キャッシュを取り除く()
    {
        var harness = new Harness();
        var config = TestData.QueryConfig();
        await harness.Configs.SaveAsync(config);
        await harness.Resolution.RefreshAsync(config);
        await harness.Supervisor.StartAsync(config);

        await harness.Configs.DeleteAsync(config.Id);

        await Assert.That(harness.Launcher.LastHandle.Stopped).IsTrue();
        await Assert.That(harness.Configs.Configs.Count).IsEqualTo(0);
        await Assert.That(harness.Resolution.GetView(config.Id)).IsNull();
    }

    [Test]
    public async Task 変更イベントが発火する()
    {
        var harness = new Harness();
        var raised = 0;
        harness.Configs.ConfigsChanged += (_, _) => raised++;

        await harness.Configs.SaveAsync(TestData.StaticConfig());
        await harness.Configs.LoadAsync();

        await Assert.That(raised).IsEqualTo(2);
    }
}
