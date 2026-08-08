using Shionji.Domain.Resolution;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

/// <summary>保存する前に、入力した条件で実際にリソースを探せるか確かめる機能。</summary>
public class SearchTestTests
{
    private static ConfigEditorViewModel NewEditor(UiHarness ui)
    {
        ui.Main.AddConfigCommand.Execute(null);
        var editor = ui.EditorWindow.Last;
        editor.Profile = "dev";
        editor.Region = "ap-northeast-1";
        return editor;
    }

    [Test]
    public async Task 直接指定では検索テストを出さない()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        editor.DestinationKind = DestinationKind.Static;
        editor.GatewayKind = GatewayKind.Ec2ById;

        await Assert.That(editor.CanTestSearch).IsFalse();
    }

    [Test]
    public async Task 検索条件を使う設定では検索テストを出す()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        editor.DestinationKind = DestinationKind.ElastiCache;

        await Assert.That(editor.CanTestSearch).IsTrue();
    }

    [Test]
    public async Task 転送先と踏み台の両方を検索して結果を出す()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        editor.DestinationKind = DestinationKind.ElastiCache;
        editor.DestNamePattern = "prod-redis*";
        editor.GatewayKind = GatewayKind.Ec2ByQuery;
        editor.GwNamePattern = "bastion-*";

        await editor.TestSearchCommand.ExecuteAsync(null);

        await Assert.That(ui.App.Catalog.CallCount).IsEqualTo(2);
        await Assert.That(editor.SearchTestFailed).IsFalse();
        await Assert.That(editor.SearchTestResult!).Contains("転送先: cache-1 が見つかりました");
        await Assert.That(editor.SearchTestResult!).Contains("踏み台: ec2-1 が見つかりました");
    }

    [Test]
    public async Task 見つからなければ失敗として示す()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (_, _) => ResolutionOutcome.NotFound.Instance;
        var editor = NewEditor(ui);
        editor.DestinationKind = DestinationKind.Aurora;

        await editor.TestSearchCommand.ExecuteAsync(null);

        await Assert.That(editor.SearchTestFailed).IsTrue();
        await Assert.That(editor.SearchTestResult!).Contains("条件に一致するリソースがありません");
    }

    [Test]
    public async Task 複数一致なら候補を並べる()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (_, _) => new ResolutionOutcome.Ambiguous(
        [
            TestData.Resource("redis-a", host: "a.cache.example"),
            TestData.Resource("redis-b", host: "b.cache.example"),
        ]);
        var editor = NewEditor(ui);
        editor.DestinationKind = DestinationKind.ElastiCache;

        await editor.TestSearchCommand.ExecuteAsync(null);

        await Assert.That(editor.SearchTestFailed).IsTrue();
        await Assert.That(editor.SearchTestResult!).Contains("2 件が一致しました");
        await Assert.That(editor.SearchTestCandidates.Count).IsEqualTo(2);
        await Assert.That(editor.SearchTestCandidates[0]).IsEqualTo("redis-a (a.cache.example)");
    }

    [Test]
    public async Task プロファイル未入力なら検索せずに知らせる()
    {
        var ui = new UiHarness();
        ui.Main.AddConfigCommand.Execute(null);
        var editor = ui.EditorWindow.Last;
        editor.DestinationKind = DestinationKind.ElastiCache;

        await editor.TestSearchCommand.ExecuteAsync(null);

        await Assert.That(editor.SearchTestFailed).IsTrue();
        await Assert.That(ui.App.Catalog.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task 検索が例外を投げても失敗として扱う()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (_, _) => throw new InvalidOperationException("boom");
        var editor = NewEditor(ui);
        editor.DestinationKind = DestinationKind.Ec2;

        await editor.TestSearchCommand.ExecuteAsync(null);

        await Assert.That(editor.SearchTestFailed).IsTrue();
        await Assert.That(editor.SearchTestResult!).Contains("boom");
    }
}
