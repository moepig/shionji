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

    /// <summary>保存が通る最小限の入力。着目する項目だけを試験本体で足す。</summary>
    private static void FillStatic(ConfigEditorViewModel editor)
    {
        editor.Name = "api-db";
        editor.Profile = "dev";
        editor.DestinationKind = DestinationKind.Static;
        editor.DestHost = "db.example.internal";
        editor.DestPortText = "3306";
        editor.GatewayKind = GatewayKind.Ec2ById;
        editor.GwInstanceId = "i-0123456789abcdef0";
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
    public async Task コマンドを並べた順に保存できる()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        FillStatic(editor);
        editor.AddCommandCommand.Execute(null);
        editor.Commands[0].Label = "MySQL";
        editor.Commands[0].CommandLine = "mysql -h {host} -P {port}";
        editor.AddCommandCommand.Execute(null);
        editor.Commands[1].CommandLine = "http://{host}:{port}/";

        await editor.SaveCommand.ExecuteAsync(null);

        var commands = ui.App.Configs.Configs.Single().Commands.Items;
        await Assert.That(commands.Select(c => c.Label))
            .IsEquivalentTo(["MySQL", "http://{host}:{port}/"]);
        // 表示名が空ならコマンドがそのまま名前になる
        await Assert.That(commands[1].CommandLine).IsEqualTo("http://{host}:{port}/");
    }

    [Test]
    public async Task 未入力のコマンド行は保存しない()
    {
        // 追加ボタンだけ押して埋めなかった行を残さない
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        FillStatic(editor);
        editor.AddCommandCommand.Execute(null);
        editor.AddCommandCommand.Execute(null);
        editor.Commands[0].CommandLine = "notepad";

        await editor.SaveCommand.ExecuteAsync(null);

        await Assert.That(ui.App.Configs.Configs.Single().Commands.Items.Single().CommandLine)
            .IsEqualTo("notepad");
    }

    [Test]
    public async Task 表示名だけの行は検証エラーになり保存されない()
    {
        // 実行する内容が無い行を黙って捨てない
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        FillStatic(editor);
        editor.AddCommandCommand.Execute(null);
        editor.Commands[0].Label = "MySQL";

        await editor.SaveCommand.ExecuteAsync(null);

        await Assert.That(editor.ValidationError!).Contains("コマンド");
        await Assert.That(ui.App.Configs.Configs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task 編集で開くと登録済みのコマンドが入っている()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(
            name: "api-db", commands: [TestData.Command("mysql -P {port}", "MySQL")]));
        ui.Main.SelectedRow = ui.Main.Rows[0];

        ((ConfigDetailViewModel)ui.Main.DetailContent!).EditCommand.Execute(null);
        var editor = ui.EditorWindow.Last;

        await Assert.That(editor.Commands.Single().Label).IsEqualTo("MySQL");
        await Assert.That(editor.Commands.Single().CommandLine).IsEqualTo("mysql -P {port}");

        editor.Commands[0].RemoveCommand.Execute(null);
        await editor.SaveCommand.ExecuteAsync(null);

        await Assert.That(ui.App.Configs.Configs.Single().Commands.IsEmpty).IsTrue();
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
        editor.AddDestTagCommand.Execute(null);
        editor.DestTags[0].Key = "Environment";
        editor.DestTags[0].Value = "production";
        editor.AddDestTagCommand.Execute(null);
        editor.DestTags[1].Key = "Team";
        editor.DestTags[1].Value = "platform";
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
    public async Task タグ条件は行ごとに追加削除できる()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        editor.DestinationKind = DestinationKind.Ec2;

        editor.AddDestTagCommand.Execute(null);
        editor.AddDestTagCommand.Execute(null);
        await Assert.That(editor.DestTags.Count).IsEqualTo(2);

        editor.DestTags[1].RemoveCommand.Execute(null);
        await Assert.That(editor.DestTags.Count).IsEqualTo(1);
    }

    [Test]
    public async Task 空の行は無視し片方だけの行はエラーにする()
    {
        var ui = new UiHarness();
        var editor = NewEditor(ui);
        editor.Name = "x";
        editor.Profile = "dev";
        editor.DestinationKind = DestinationKind.Ec2;
        editor.DestPortText = "22";
        editor.GatewayKind = GatewayKind.Direct;

        // 空行だけなら条件なしとして通る
        editor.AddDestTagCommand.Execute(null);
        var built = editor.Build();
        await Assert.That(built.IsSuccess).IsTrue();
        await Assert.That(((Destination.Query)built.Value.Destination).ResourceQuery.Tags.IsEmpty).IsTrue();

        // キーだけ埋まっている行はエラー
        editor.DestTags[0].Key = "Environment";
        var invalid = editor.Build();
        await Assert.That(invalid.IsFailure).IsTrue();
        await Assert.That(invalid.Error).Contains("タグ条件");
    }
}
