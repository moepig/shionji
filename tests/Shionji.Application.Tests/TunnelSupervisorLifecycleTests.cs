using Shionji.Domain.Tunneling;
using Shionji.TestSupport;

namespace Shionji.Application.Tests;

/// <summary>
/// 接続処理と停止要求が競合したときの後始末を検証する。
/// ここが漏れるとトンネルのプロセスやポートが残り続ける。
/// </summary>
public class TunnelSupervisorLifecycleTests
{
    /// <summary>任意のタイミングで解放できるゲート。</summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Wait()
        {
            _entered.TrySetResult();
            return _tcs.Task;
        }

        public Task Entered => _entered.Task;

        public void Release() => _tcs.TrySetResult();
    }

    [Test]
    public async Task 解決中に停止するとトンネルは起動されない()
    {
        var harness = new Harness();
        var gate = new Gate();
        harness.Catalog.Gate = gate.Wait;
        var config = TestData.QueryConfig();

        var starting = harness.Supervisor.StartAsync(config);
        await gate.Entered;
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Resolving>();

        await harness.Supervisor.StopAsync(config.Id);
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();

        gate.Release();
        await starting;

        await Assert.That(harness.Launcher.LaunchCount).IsEqualTo(0);
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();
    }

    [Test]
    public async Task 起動完了直前に停止されたトンネルは畳まれる()
    {
        var harness = new Harness();
        var gate = new Gate();
        harness.Launcher.Gate = gate.Wait;
        var config = TestData.StaticConfig();

        var starting = harness.Supervisor.StartAsync(config);
        await gate.Entered;
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Starting>();

        await harness.Supervisor.StopAsync(config.Id);

        // 停止後に起動が完了しても、掴んだハンドルは放置せず閉じる
        gate.Release();
        await starting;

        await Assert.That(harness.Launcher.LastHandle.Stopped).IsTrue();
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();
    }

    [Test]
    public async Task 停止直後に再接続できる()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();
        await harness.Supervisor.StartAsync(config);
        await harness.Supervisor.StopAsync(config.Id);

        await harness.Supervisor.StartAsync(config);

        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Established>();
        await Assert.That(harness.Launcher.LaunchCount).IsEqualTo(2);
    }

    [Test]
    public async Task 未接続の設定を停止しても何も起きない()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();

        await harness.Supervisor.StopAsync(config.Id);

        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();
        await Assert.That(harness.Supervisor.GetLocalPort(config.Id)).IsNull();
    }

    [Test]
    public async Task StopAllで全セッションが閉じられる()
    {
        var harness = new Harness();
        var a = TestData.StaticConfig(name: "a", localPort: 15001);
        var b = TestData.StaticConfig(name: "b", localPort: 15002);
        await harness.Supervisor.StartAsync(a);
        await harness.Supervisor.StartAsync(b);

        await harness.Supervisor.StopAllAsync();

        await Assert.That(harness.Supervisor.GetState(a.Id)).IsTypeOf<SessionState.Idle>();
        await Assert.That(harness.Supervisor.GetState(b.Id)).IsTypeOf<SessionState.Idle>();
        await Assert.That(harness.Launcher.Handles.All(h => h.Stopped)).IsTrue();
    }

    [Test]
    public async Task Disposeで全セッションが閉じられる()
    {
        // アプリ終了時にトンネルを残さないこと
        var harness = new Harness();
        var config = TestData.StaticConfig();
        await harness.Supervisor.StartAsync(config);

        await harness.Supervisor.DisposeAsync();

        await Assert.That(harness.Launcher.LastHandle.Stopped).IsTrue();
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();
    }

    [Test]
    public async Task 確立していなければローカルポートは公開されない()
    {
        var harness = new Harness();
        var gate = new Gate();
        harness.Launcher.Gate = gate.Wait;
        var config = TestData.StaticConfig();

        var starting = harness.Supervisor.StartAsync(config);
        await gate.Entered;

        await Assert.That(harness.Supervisor.GetLocalPort(config.Id)).IsNull();

        gate.Release();
        await starting;
        await Assert.That(harness.Supervisor.GetLocalPort(config.Id)!.Value).IsEqualTo(15432);
    }
}
