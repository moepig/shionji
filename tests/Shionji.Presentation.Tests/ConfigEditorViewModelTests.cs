using Shionji.Domain.Configuration;
using Shionji.Domain.Tunneling;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

public class ConfigEditorViewModelTests
{
    private static ConfigEditorViewModel NewEditor(UiHarness ui)
    {
        ui.Main.AddConfigCommand.Execute(null);
        return ui.EditorWindow.Last;
    }

    [Test]
    public async Task 直接指定の設定を入力して保存できる()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        editor.Name = "api-db";
        editor.Profile = "dev";
        editor.Region = "ap-northeast-1";
        editor.LocalPortText = "13306";
        editor.DestinationKind = DestinationKind.Static;
        editor.DestHost = "db.example.internal";
        editor.DestPortText = "3306";
        editor.GatewayKind = GatewayKind.Ec2ById;
        editor.GwInstanceId = "i-0123456789abcdef0";
        editor.AutoReconnect = true;

        await editor.SaveCommand.ExecuteAsync(null);

        await Assert.That(editor.ValidationError).IsNull();
        var saved = ui.App.Configs.Configs.Single();
        await Assert.That(saved.Name.Value).IsEqualTo("api-db");
        await Assert.That(saved.Options.AutoReconnect).IsTrue();
        // 保存後は行が選択され詳細に戻る
        await Assert.That(ui.Main.SelectedRow!.Name).IsEqualTo("api-db");
        await Assert.That(ui.Main.DetailContent).IsTypeOf<ConfigDetailViewModel>();
    }

    [Test]
    public async Task 不正なポートは検証エラーになり保存されない()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        editor.Name = "x";
        editor.Profile = "dev";
        editor.DestinationKind = DestinationKind.Static;
        editor.DestHost = "db.example.internal";
        editor.DestPortText = "abc";
        editor.GatewayKind = GatewayKind.Ec2ById;
        editor.GwInstanceId = "i-0123456789abcdef0";

        await editor.SaveCommand.ExecuteAsync(null);

        await Assert.That(editor.ValidationError).IsNotNull();
        await Assert.That(ui.App.Configs.Configs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ドメインの不変条件違反も検証エラーとして表示される()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        editor.Name = "x";
        editor.Profile = "dev";
        editor.DestinationKind = DestinationKind.Static;
        editor.DestHost = "db.example.internal";
        editor.DestPortText = "5432";
        editor.GatewayKind = GatewayKind.Direct; // 直接指定 + Direct は不正

        await editor.SaveCommand.ExecuteAsync(null);

        await Assert.That(editor.ValidationError).IsNotNull();
        await Assert.That(editor.ValidationError!).Contains("踏み台");
    }

    [Test]
    public async Task クエリ転送先の設定を組み立てられる()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        editor.Name = "cache";
        editor.Profile = "dev";
        editor.LocalPortText = string.Empty; // 自動割当
        editor.DestinationKind = DestinationKind.ElastiCache;
        editor.DestNamePattern = "prod-redis*";
        editor.DestTagsText = "Environment=production|staging; Team=platform";
        editor.CacheRole = CacheEndpointRole.Reader;
        editor.DestPortText = string.Empty; // 既定ポート
        editor.GatewayKind = GatewayKind.Ec2ByQuery;
        editor.GwNamePattern = "bastion-*";

        var built = editor.Build();

        await Assert.That(built.IsSuccess).IsTrue();
        var config = built.Value;
        await Assert.That(config.LocalPort).IsTypeOf<LocalPortSpec.Auto>();
        var query = (Destination.Query)config.Destination;
        var cache = (ElastiCacheQuery)query.ResourceQuery;
        await Assert.That(cache.Role).IsEqualTo(CacheEndpointRole.Reader);
        await Assert.That(cache.Tags.Items.Count).IsEqualTo(2);
        await Assert.That(query.Port).IsTypeOf<PortSelection.FromResource>();
    }

    [Test]
    public async Task 既存設定の編集は値が復元される()
    {
        var ui = new UiHarness();
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        detail.EditCommand.Execute(null);

        var editor = ui.EditorWindow.Last;
        await Assert.That(editor.IsNew).IsFalse();
        await Assert.That(editor.Name).IsEqualTo("query-test");
        await Assert.That(editor.DestinationKind).IsEqualTo(DestinationKind.ElastiCache);
        await Assert.That(editor.GatewayKind).IsEqualTo(GatewayKind.Ec2ByQuery);

        // ラウンドトリップ: 編集せず保存しても等価な設定になる
        var rebuilt = editor.Build();
        await Assert.That(rebuilt.IsSuccess).IsTrue();
        await Assert.That(rebuilt.Value).IsEqualTo(config);
    }

    [Test]
    public async Task 接続中の設定を編集して保存すると切断される()
    {
        var ui = new UiHarness();
        var config = TestData.StaticConfig(name: "api-db");
        await ui.App.Configs.SaveAsync(config);
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        detail.EditCommand.Execute(null);
        var editor = ui.EditorWindow.Last;
        editor.Name = "api-db-2";

        await editor.SaveCommand.ExecuteAsync(null);

        await Assert.That(ui.App.Launcher.LastHandle.Stopped).IsTrue();
        await Assert.That(ui.App.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();
        await Assert.That(ui.App.Configs.Configs.Single().Name.Value).IsEqualTo("api-db-2");
    }

    [Test]
    public async Task タグ条件の書式()
    {
        var tags = ConfigEditorViewModel.ParseTags("Env=prod|staging; Team=platform");
        await Assert.That(tags.Items.Count).IsEqualTo(2);
        await Assert.That(tags.Items[0].Values).IsEquivalentTo(["prod", "staging"]);

        await Assert.That(ConfigEditorViewModel.ParseTags("  ").IsEmpty).IsTrue();
        await Assert.That(ConfigEditorViewModel.FormatTags(tags)).IsEqualTo("Env=prod|staging; Team=platform");
    }
}
