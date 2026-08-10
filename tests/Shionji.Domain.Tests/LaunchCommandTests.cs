using Shionji.Domain.Configuration;

namespace Shionji.Domain.Tests;

/// <summary>接続中に実行できるコマンドの値オブジェクト。</summary>
public class LaunchCommandTests
{
    [Test]
    public async Task 前後の空白は落とされる()
    {
        var command = LaunchCommand.Create("  MySQL  ", "  mysql -P {port}  ").Value;

        await Assert.That(command.Label).IsEqualTo("MySQL");
        await Assert.That(command.CommandLine).IsEqualTo("mysql -P {port}");
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task コマンドが空なら作れない(string commandLine)
    {
        var result = LaunchCommand.Create("MySQL", commandLine);

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task 表示名が空ならコマンドがそのまま名前になる()
    {
        // ボタンに何も出ない状態を作らないため
        var command = LaunchCommand.Create(string.Empty, "notepad").Value;

        await Assert.That(command.Label).IsEqualTo("notepad");
    }

    [Test]
    public async Task 表示名が長すぎると作れない()
    {
        var result = LaunchCommand.Create(new string('a', 65), "notepad");

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task 並びは内容が同じなら等しい()
    {
        // 設定の保存と復元で、同じ内容が同じものとして扱われること
        var a = LaunchCommands.From([LaunchCommand.Create("MySQL", "mysql -P {port}").Value]);
        var b = LaunchCommands.From([LaunchCommand.Create("MySQL", "mysql -P {port}").Value]);
        var reordered = LaunchCommands.From(
        [
            LaunchCommand.Create("MySQL", "mysql -P {port}").Value,
            LaunchCommand.Create("Redis", "redis-cli -p {port}").Value,
        ]);

        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a).IsNotEqualTo(reordered);
        await Assert.That(LaunchCommands.From([]).IsEmpty).IsTrue();
    }
}
