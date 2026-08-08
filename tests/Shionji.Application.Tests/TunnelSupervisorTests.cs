using Shionji.TestSupport;
using Shionji.Domain.Primitives;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;

namespace Shionji.Application.Tests;

public class TunnelSupervisorTests
{
    [Test]
    public async Task 直接指定の設定を接続すると確立する()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();

        await harness.Supervisor.StartAsync(config);

        var state = harness.Supervisor.GetState(config.Id);
        var established = (SessionState.Established)state;
        await Assert.That(established.Plan.Target.Value).IsEqualTo("i-0123456789abcdef0");
        var mode = (SessionMode.RemoteHostForward)established.Plan.Mode;
        await Assert.That(mode.Host.Value).IsEqualTo("db.example.internal");
        await Assert.That(established.Plan.LocalPort.Value).IsEqualTo(15432);
        await Assert.That(harness.Supervisor.GetLocalPort(config.Id)!.Value).IsEqualTo(15432);

        var events = harness.EventsFor(config.Id);
        await Assert.That(events[0]).IsTypeOf<SessionState.Resolving>();
        await Assert.That(events[1]).IsTypeOf<SessionState.Starting>();
        await Assert.That(events[2]).IsTypeOf<SessionState.Established>();
    }

    [Test]
    public async Task クエリ設定は転送先と踏み台を解決してから接続する()
    {
        var harness = new Harness();
        var config = TestData.QueryConfig();

        await harness.Supervisor.StartAsync(config);

        await Assert.That(harness.Catalog.CallCount).IsEqualTo(2);
        var established = (SessionState.Established)harness.Supervisor.GetState(config.Id);
        await Assert.That(established.Plan.Target.Value).IsEqualTo("i-0feedfacefeedface");
        var mode = (SessionMode.RemoteHostForward)established.Plan.Mode;
        await Assert.That(mode.Host.Value).IsEqualTo("redis.prod.example.com");
        await Assert.That(mode.RemotePort.Value).IsEqualTo(6379);
        await Assert.That(established.Plan.LocalPort.Value).IsEqualTo(50000);

        // 接続時の解決結果がリスト表示キャッシュにも反映される
        var view = harness.Resolution.GetView(config.Id);
        await Assert.That(view).IsNotNull();
        await Assert.That(view!.Destination).IsTypeOf<ResolutionOutcome.Resolved>();
        await Assert.That(view.Gateway).IsTypeOf<ResolutionOutcome.Resolved>();
    }

    [Test]
    public async Task 転送先が見つからなければ失敗し起動しない()
    {
        var harness = new Harness();
        harness.Catalog.Handler = (_, _) => ResolutionOutcome.NotFound.Instance;
        var config = TestData.QueryConfig();

        await harness.Supervisor.StartAsync(config);

        var failed = (SessionState.Failed)harness.Supervisor.GetState(config.Id);
        await Assert.That(failed.Error.Code).IsEqualTo("NotFound");
        await Assert.That(failed.Error.Phase).IsEqualTo(FailurePhase.ResolveDestination);
        await Assert.That(harness.Launcher.LaunchCount).IsEqualTo(0);
    }

    [Test]
    public async Task 複数一致は失敗しビューに候補が残る()
    {
        var harness = new Harness();
        var candidates = new[] { TestData.Resource("a"), TestData.Resource("b") };
        harness.Catalog.Handler = (_, _) => new ResolutionOutcome.Ambiguous(candidates);
        var config = TestData.QueryConfig();

        await harness.Supervisor.StartAsync(config);

        var failed = (SessionState.Failed)harness.Supervisor.GetState(config.Id);
        await Assert.That(failed.Error.Code).IsEqualTo("Ambiguous");
        var view = harness.Resolution.GetView(config.Id);
        var ambiguous = (ResolutionOutcome.Ambiguous)view!.Destination!;
        await Assert.That(ambiguous.Candidates.Count).IsEqualTo(2);
    }

    [Test]
    public async Task 起動失敗はそのエラーを保持して失敗する()
    {
        var harness = new Harness();
        var error = TestData.Error(FailurePhase.StartSession);
        harness.Launcher.Handler = _ => Result<ITunnelHandle, ErrorDetail>.Failure(error);
        var config = TestData.StaticConfig();

        await harness.Supervisor.StartAsync(config);

        var failed = (SessionState.Failed)harness.Supervisor.GetState(config.Id);
        await Assert.That(failed.Error).IsEqualTo(error);
    }

    [Test]
    public async Task 固定ローカルポートが使用中なら失敗する()
    {
        var harness = new Harness();
        harness.Probe.BusyPorts.Add(15432);
        var config = TestData.StaticConfig();

        await harness.Supervisor.StartAsync(config);

        var failed = (SessionState.Failed)harness.Supervisor.GetState(config.Id);
        await Assert.That(failed.Error.Code).IsEqualTo("LocalPortInUse");
        await Assert.That(harness.Launcher.LaunchCount).IsEqualTo(0);
    }

    [Test]
    public async Task 接続済みの設定への接続要求は無視される()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();

        await harness.Supervisor.StartAsync(config);
        await harness.Supervisor.StartAsync(config);

        await Assert.That(harness.Launcher.LaunchCount).IsEqualTo(1);
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Established>();
    }

    [Test]
    public async Task 停止でトンネルを畳んでIdleに戻る()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();
        await harness.Supervisor.StartAsync(config);

        await harness.Supervisor.StopAsync(config.Id);

        await Assert.That(harness.Launcher.LastHandle.Stopped).IsTrue();
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();
        var events = harness.EventsFor(config.Id);
        await Assert.That(events.Any(e => e is SessionState.Closing)).IsTrue();
    }

    [Test]
    public async Task 予期せぬ終了は自動再接続される()
    {
        var scheduler = new ImmediateScheduler();
        var harness = new Harness(scheduler);
        var config = TestData.StaticConfig(autoReconnect: true);
        await harness.Supervisor.StartAsync(config);
        var firstHandle = harness.Launcher.LastHandle;

        firstHandle.TriggerExit(TestData.Error());
        await Wait.UntilAsync(() =>
            harness.Launcher.LaunchCount == 2 &&
            harness.Supervisor.GetState(config.Id) is SessionState.Established);

        await Assert.That(scheduler.Delays).IsEquivalentTo([TimeSpan.FromSeconds(2)]);
        var events = harness.EventsFor(config.Id);
        await Assert.That(events.Any(e => e is SessionState.Reconnecting)).IsTrue();
    }

    [Test]
    public async Task 自動再接続無効なら予期せぬ終了で失敗のまま()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig(autoReconnect: false);
        await harness.Supervisor.StartAsync(config);

        harness.Launcher.LastHandle.TriggerExit(TestData.Error());
        await Wait.UntilAsync(() => harness.Supervisor.GetState(config.Id) is SessionState.Failed);

        await Assert.That(harness.Launcher.LaunchCount).IsEqualTo(1);
    }

    [Test]
    public async Task 再接続は上限までバックオフしながら試行し最後は失敗する()
    {
        var scheduler = new ImmediateScheduler();
        var harness = new Harness(scheduler);
        var config = TestData.StaticConfig(autoReconnect: true);
        await harness.Supervisor.StartAsync(config);

        // 以後の起動はすべて失敗させる
        var error = TestData.Error(FailurePhase.StartSession);
        harness.Launcher.Handler = _ => Result<ITunnelHandle, ErrorDetail>.Failure(error);

        harness.Launcher.Handles[0].TriggerExit(TestData.Error());
        await Wait.UntilAsync(() => harness.Supervisor.GetState(config.Id) is SessionState.Failed);

        await Assert.That(scheduler.Delays).IsEquivalentTo(
        [
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(16),
            TimeSpan.FromSeconds(30),
        ]);
        await Assert.That(harness.Launcher.LaunchCount).IsEqualTo(6);
    }

    [Test]
    public async Task 再試行待ち中の停止はIdleに戻し再試行しない()
    {
        var scheduler = new BlockingScheduler();
        var harness = new Harness(scheduler);
        var config = TestData.StaticConfig(autoReconnect: true);
        await harness.Supervisor.StartAsync(config);

        harness.Launcher.LastHandle.TriggerExit(TestData.Error());
        await Wait.UntilAsync(() => harness.Supervisor.GetState(config.Id) is SessionState.Reconnecting);

        await harness.Supervisor.StopAsync(config.Id);
        await Assert.That(harness.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();

        scheduler.Release();
        await Task.Delay(50);
        await Assert.That(harness.Launcher.LaunchCount).IsEqualTo(1);
    }

    [Test]
    public async Task トンネルのログが設定IDつきで転送される()
    {
        var harness = new Harness();
        var config = TestData.StaticConfig();
        var logs = new List<SessionLogEventArgs>();
        harness.Supervisor.SessionLog += (_, e) => logs.Add(e);
        await harness.Supervisor.StartAsync(config);

        harness.Launcher.LastHandle.EmitLog("Port 15432 opened for sessionId test.");

        await Assert.That(logs.Count).IsEqualTo(1);
        await Assert.That(logs[0].ConfigId).IsEqualTo(config.Id);
        await Assert.That(logs[0].IsError).IsFalse();
    }
}
