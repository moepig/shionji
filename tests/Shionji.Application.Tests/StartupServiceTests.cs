using Shionji.TestSupport;
using Shionji.Domain.Tunneling;

namespace Shionji.Application.Tests;

public class StartupServiceTests
{
    [Test]
    public async Task 起動時に全件解決し自動接続対象のみ接続する()
    {
        var auto = TestData.StaticConfig(connectOnLaunch: true, localPort: 15001, name: "auto");
        var manual = TestData.StaticConfig(connectOnLaunch: false, localPort: 15002, name: "manual");
        var repo = new InMemoryRepository(auto, manual);
        var harness = new Harness(repository: repo);
        var startup = new StartupService(harness.Configs, harness.Resolution, harness.Supervisor);

        await startup.RunAsync();

        await Assert.That(harness.Configs.Configs.Count).IsEqualTo(2);
        await Assert.That(harness.Resolution.GetView(manual.Id)).IsNotNull();
        await Assert.That(harness.Supervisor.GetState(auto.Id)).IsTypeOf<SessionState.Established>();
        await Assert.That(harness.Supervisor.GetState(manual.Id)).IsTypeOf<SessionState.Idle>();
        await Assert.That(harness.Launcher.LaunchCount).IsEqualTo(1);
    }

    [Test]
    public async Task 自動接続する設定のクエリは二重に解決しない()
    {
        var auto = TestData.QueryConfig(connectOnLaunch: true);
        var repo = new InMemoryRepository(auto);
        var harness = new Harness(repository: repo);
        var startup = new StartupService(harness.Configs, harness.Resolution, harness.Supervisor);

        await startup.RunAsync();

        // 転送先 + 踏み台で 1 回ずつ (事前の全件解決と接続時解決の二重呼び出しをしない)
        await Assert.That(harness.Catalog.CallCount).IsEqualTo(2);
        await Assert.That(harness.Supervisor.GetState(auto.Id)).IsTypeOf<SessionState.Established>();
        // 接続フローの Publish によりビューは埋まっている
        await Assert.That(harness.Resolution.GetView(auto.Id)).IsNotNull();
    }
}
