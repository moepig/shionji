using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

/// <summary>接続先設定に登録し、詳細ペインから実行するコマンド。</summary>
public class ExternalCommandTests
{
    [Test]
    public async Task 登録が無ければ節ごと出さない()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.HasCommands).IsFalse();
        await Assert.That(detail.Commands).IsEmpty();
    }

    [Test]
    public async Task 登録した順にボタンが並ぶ()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", commands:
        [
            TestData.Command("mysql -h {host} -P {port}", "MySQL"),
            TestData.Command("http://{host}:{port}/", "ブラウザ"),
        ]));
        ui.Main.SelectedRow = ui.Main.Rows[0];

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.HasCommands).IsTrue();
        await Assert.That(detail.Commands.Select(c => c.Label)).IsEquivalentTo(["MySQL", "ブラウザ"]);
        // 登録内容はそのまま出し、実行時に差し込むことが分かるようにする
        await Assert.That(detail.Commands[0].CommandLine).IsEqualTo("mysql -h {host} -P {port}");
    }

    [Test]
    public async Task 接続先設定ごとに別のコマンドが並ぶ()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(
            name: "api-db", localPort: 15001, commands: [TestData.Command("mysql -P {port}", "MySQL")]));
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(
            name: "cache", localPort: 15002, commands: [TestData.Command("redis-cli -p {port}", "Redis")]));

        ui.Main.SelectedRow = ui.Main.Rows.Single(r => r.Name == "api-db");
        var apiDetail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(apiDetail.Commands.Single().Label).IsEqualTo("MySQL");

        ui.Main.SelectedRow = ui.Main.Rows.Single(r => r.Name == "cache");
        var cacheDetail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(cacheDetail.Commands.Single().Label).IsEqualTo("Redis");
    }

    [Test]
    public async Task 接続していない間は実行できない()
    {
        // 差し込むローカル側のポートが決まらないため
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(
            name: "api-db", localPort: 13306, commands: [TestData.Command("mysql -h {host} -P {port}", "MySQL")]));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await Assert.That(detail.CanRunCommands).IsFalse();
        await Assert.That(detail.Commands[0].IsEnabled).IsFalse();

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(detail.CanRunCommands).IsTrue();
        await Assert.That(detail.Commands[0].IsEnabled).IsTrue();

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(detail.CanRunCommands).IsFalse();
    }

    [Test]
    public async Task 実行すると待ち受けているローカル側の値が入る()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(
            name: "api-db", localPort: 13306,
            commands: [TestData.Command("mysql -h {host} -P {port} -u app", "MySQL")]));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        detail.Commands[0].RunCommand.Execute(null);

        var launched = ui.CommandLauncher.Launched.Single();
        await Assert.That(launched.FileName).IsEqualTo("mysql");
        await Assert.That(launched.Arguments).IsEqualTo("-h localhost -P 13306 -u app");
        await Assert.That(detail.CommandError).IsNull();
    }

    [Test]
    public async Task 自動割当でも実際に割り当たったポートが入る()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.QueryConfig(
            commands: [TestData.Command("curl http://{host}:{port}/health", "確認")]));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        detail.Commands[0].RunCommand.Execute(null);

        await Assert.That(ui.CommandLauncher.Launched.Single().Arguments)
            .IsEqualTo("http://localhost:50000/health");
    }

    [Test]
    public async Task 接続していなければ押されても起動しない()
    {
        // ボタンは無効だが、束縛の隙間で押されても実行しない
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(
            name: "api-db", commands: [TestData.Command("mysql -P {port}", "MySQL")]));
        ui.Main.SelectedRow = ui.Main.Rows[0];

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        detail.Commands[0].RunCommand.Execute(null);

        await Assert.That(ui.CommandLauncher.Launched).IsEmpty();
    }

    [Test]
    public async Task 起動できなかった理由は詳細に出す()
    {
        var ui = new UiHarness();
        ui.CommandLauncher.Error = "コマンドを実行できません: 指定されたファイルが見つかりません。";
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(
            name: "api-db", commands: [TestData.Command("mysql -P {port}", "MySQL")]));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        detail.Commands[0].RunCommand.Execute(null);

        await Assert.That(detail.CommandError!).Contains("見つかりません");
    }

    [Test]
    public async Task 編集で足したコマンドはボタンに反映される()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        await Assert.That(((ConfigDetailViewModel)ui.Main.DetailContent!).HasCommands).IsFalse();

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        detail.EditCommand.Execute(null);
        var editor = ui.EditorWindow.Last;
        editor.AddCommandCommand.Execute(null);
        editor.Commands[0].Label = "MySQL";
        editor.Commands[0].CommandLine = "mysql -P {port}";
        await editor.SaveCommand.ExecuteAsync(null);

        var updated = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(updated.HasCommands).IsTrue();
        await Assert.That(updated.Commands.Single().Label).IsEqualTo("MySQL");
    }
}
