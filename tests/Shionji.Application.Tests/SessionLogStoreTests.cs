using Shionji.TestSupport;

namespace Shionji.Application.Tests;

public class SessionLogStoreTests
{
    [Test]
    public async Task 設定ごとにログが分かれる()
    {
        var harness = new Harness();
        var a = TestData.StaticConfig(name: "a", localPort: 15001);
        var b = TestData.StaticConfig(name: "b", localPort: 15002);
        await harness.Supervisor.StartAsync(a);
        var handleA = harness.Launcher.LastHandle;
        await harness.Supervisor.StartAsync(b);
        var handleB = harness.Launcher.LastHandle;

        handleA.EmitLog("from a");
        handleB.EmitLog("from b", isError: true);

        await Assert.That(harness.Logs.GetLines(a.Id).Single().Line).IsEqualTo("from a");
        var lineB = harness.Logs.GetLines(b.Id).Single();
        await Assert.That(lineB.Line).IsEqualTo("from b");
        await Assert.That(lineB.IsError).IsTrue();
    }

    [Test]
    public async Task 末尾200行に丸められる()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();
        await harness.Supervisor.StartAsync(config);
        var handle = harness.Launcher.LastHandle;

        for (var i = 0; i < 250; i++)
            handle.EmitLog($"line {i}");

        var lines = harness.Logs.GetLines(config.Id);
        await Assert.That(lines.Count).IsEqualTo(200);
        await Assert.That(lines[0].Line).IsEqualTo("line 50");
        await Assert.That(lines[^1].Line).IsEqualTo("line 249");
    }

    [Test]
    public async Task 未知の設定は空を返す()
    {
        var harness = new Harness();
        await Assert.That(harness.Logs.GetLines(TestData.StaticConfig().Id).Count).IsEqualTo(0);
    }

    [Test]
    public async Task 設定を削除するとログも消える()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();
        await harness.Configs.SaveAsync(config);
        await harness.Supervisor.StartAsync(config);
        harness.Launcher.LastHandle.EmitLog("hello");
        await Assert.That(harness.Logs.GetLines(config.Id).Count).IsEqualTo(1);

        await harness.Configs.DeleteAsync(config.Id);

        await Assert.That(harness.Logs.GetLines(config.Id).Count).IsEqualTo(0);
    }

    [Test]
    public async Task 追記のたびにイベントが発火する()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();
        await harness.Supervisor.StartAsync(config);
        var received = new List<string>();
        harness.Logs.LineAppended += (_, e) => received.Add(e.Line);

        harness.Launcher.LastHandle.EmitLog("one");
        harness.Launcher.LastHandle.EmitLog("two");

        await Assert.That(received).IsEquivalentTo(["one", "two"]);
    }
}
