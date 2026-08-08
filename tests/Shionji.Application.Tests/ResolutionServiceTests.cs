using Shionji.TestSupport;
using Shionji.Domain.Resolution;

namespace Shionji.Application.Tests;

public class ResolutionServiceTests
{
    [Test]
    public async Task 直接指定と固定踏み台の設定には解決対象がない()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();

        await harness.Resolution.RefreshAsync(config);

        var view = harness.Resolution.GetView(config.Id)!;
        await Assert.That(view.IsResolving).IsFalse();
        await Assert.That(view.Destination).IsNull();
        await Assert.That(view.Gateway).IsNull();
        await Assert.That(view.RefreshedAt).IsNotNull();
        await Assert.That(harness.Catalog.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task クエリ設定の解決結果がキャッシュされ変更が通知される()
    {
        var harness = new Harness();
        var config = TestData.QueryConfig();
        var notified = 0;
        harness.Resolution.ViewChanged += (_, id) =>
        {
            if (id == config.Id)
                notified++;
        };

        await harness.Resolution.RefreshAsync(config);

        var view = harness.Resolution.GetView(config.Id)!;
        await Assert.That(view.Destination).IsTypeOf<ResolutionOutcome.Resolved>();
        await Assert.That(view.Gateway).IsTypeOf<ResolutionOutcome.Resolved>();
        // 解決中マーク + 完了 で 2 回以上
        await Assert.That(notified >= 2).IsTrue();
    }

    [Test]
    public async Task カタログの例外はFailedに変換される()
    {
        var harness = new Harness();
        harness.Catalog.Handler = (_, _) => throw new InvalidOperationException("boom");
        var config = TestData.QueryConfig();

        await harness.Resolution.RefreshAsync(config);

        var view = harness.Resolution.GetView(config.Id)!;
        var failed = (ResolutionOutcome.Failed)view.Destination!;
        await Assert.That(failed.Error.Phase).IsEqualTo(FailurePhase.ResolveDestination);
        await Assert.That(failed.Error.Message).IsEqualTo("boom");
    }

    [Test]
    public async Task 全件更新はすべての設定のビューを作る()
    {
        var harness = new Harness();
        var a = TestData.StaticConfig(name: "a");
        var b = TestData.QueryConfig();

        await harness.Resolution.RefreshAllAsync([a, b]);

        await Assert.That(harness.Resolution.GetView(a.Id)).IsNotNull();
        await Assert.That(harness.Resolution.GetView(b.Id)).IsNotNull();
    }
}
