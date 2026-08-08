using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

/// <summary>
/// 一覧と詳細の区切りを示す見出し。
/// 右ペインが空のときに「謎の空白」に見えないよう、状態が文言に出ることを確かめる。
/// </summary>
public class PaneHeaderTests
{
    [Test]
    public async Task 設定の見出しに表示件数が出る()
    {
        var ui = new UiHarness();
        await Assert.That(ui.Main.ConfigsHeader).IsEqualTo("設定 (0)");

        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "a", localPort: 15001));
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "b", localPort: 15002));
        await Assert.That(ui.Main.ConfigsHeader).IsEqualTo("設定 (2)");

        // フィルタ中は絞り込み後の件数
        ui.Main.FilterText = "a";
        await Assert.That(ui.Main.ConfigsHeader).IsEqualTo("設定 (1)");
    }

    [Test]
    public async Task 未選択なら詳細ペインは空で見出しは詳細のまま()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig());

        await Assert.That(ui.Main.DetailContent).IsNull();
        await Assert.That(ui.Main.DetailHeader).IsEqualTo("詳細");
    }

    [Test]
    public async Task 編集中は見出しが切り替わる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        await Assert.That(ui.Main.DetailHeader).IsEqualTo("詳細");

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        detail.EditCommand.Execute(null);
        await Assert.That(ui.Main.DetailHeader).IsEqualTo("詳細 — 編集");

        ui.Main.AddConfigCommand.Execute(null);
        await Assert.That(ui.Main.DetailHeader).IsEqualTo("詳細 — 新規作成");
    }

    [Test]
    public async Task 編集を終えると詳細の見出しに戻る()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        detail.EditCommand.Execute(null);

        var editor = (ConfigEditorViewModel)ui.Main.DetailContent!;
        editor.CancelCommand.Execute(null);

        await Assert.That(ui.Main.DetailHeader).IsEqualTo("詳細");
    }
}
