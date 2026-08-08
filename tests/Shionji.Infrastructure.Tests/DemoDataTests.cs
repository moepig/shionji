using Shionji.Domain.Configuration;
using Shionji.Infrastructure.Fakes;

namespace Shionji.Infrastructure.Tests;

/// <summary>
/// デモ用のシードは Result.Value で組み立てているため、不正な値を書くと
/// デモモード起動時に例外で落ちる。ここで先に検出する。
/// </summary>
public class DemoDataTests
{
    [Test]
    public async Task すべてのデモ設定が有効に組み立てられる()
    {
        var configs = DemoData.Configs();

        await Assert.That(configs.Count).IsGreaterThan(0);
        await Assert.That(configs.Select(c => c.Name.Value).Distinct().Count()).IsEqualTo(configs.Count);
    }

    [Test]
    public async Task 固定ローカルポートが重複しない()
    {
        // 重複しているとデモで 2 本目以降が LocalPortInUse になってしまう
        var ports = DemoData.Configs()
            .Select(c => c.LocalPort)
            .OfType<LocalPortSpec.Fixed>()
            .Select(p => p.Port.Value)
            .ToList();

        await Assert.That(ports.Distinct().Count()).IsEqualTo(ports.Count);
    }

    [Test]
    public async Task 起動時自動接続とエラー再現の設定が揃っている()
    {
        var configs = DemoData.Configs();

        await Assert.That(configs.Any(c => c.Options.ConnectOnLaunch)).IsTrue();
        await Assert.That(configs.Any(c => c.Options.AutoReconnect)).IsTrue();
        await Assert.That(configs.Any(c => c.Aws.Profile.Value == "expired-sso")).IsTrue();
        await Assert.That(configs.Any(c => c.Gateway is GatewaySpec.Direct)).IsTrue();
    }

    [Test]
    public async Task インメモリリポジトリはシードを返し編集を保持する()
    {
        var seed = DemoData.Configs();
        var repository = new InMemoryConfigRepository([.. seed]);

        var loaded = await repository.LoadAllAsync();
        await Assert.That(loaded.Count).IsEqualTo(seed.Count);

        await repository.DeleteAsync(seed[0].Id);
        var afterDelete = await repository.LoadAllAsync();
        await Assert.That(afterDelete.Count).IsEqualTo(seed.Count - 1);
    }
}
