using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tests;

public class TunnelSessionTests
{
    private static TunnelSession Session(bool autoReconnect = false) =>
        new(ConfigId.New(), autoReconnect);

    private static TunnelSession EstablishedSession(bool autoReconnect = false)
    {
        var session = Session(autoReconnect);
        session.RequestConnect();
        session.PlanReady(TestData.Plan());
        session.MarkEstablished(DateTimeOffset.UnixEpoch);
        return session;
    }

    [Test]
    public async Task 初期状態はIdle()
    {
        await Assert.That(Session().State).IsTypeOf<SessionState.Idle>();
    }

    [Test]
    public async Task 接続から切断までのハッピーパス()
    {
        var session = Session();

        session.RequestConnect();
        await Assert.That(session.State).IsTypeOf<SessionState.Resolving>();

        var plan = TestData.Plan();
        session.PlanReady(plan);
        await Assert.That(session.State).IsEqualTo(new SessionState.Starting(plan));

        var now = DateTimeOffset.UnixEpoch;
        session.MarkEstablished(now);
        await Assert.That(session.State).IsEqualTo(new SessionState.Established(plan, now));

        session.RequestDisconnect();
        await Assert.That(session.State).IsTypeOf<SessionState.Closing>();

        session.MarkClosed();
        await Assert.That(session.State).IsTypeOf<SessionState.Idle>();
    }

    [Test]
    public async Task 解決失敗はFailedになりエラーを保持する()
    {
        var session = Session();
        session.RequestConnect();

        var error = TestData.Error(FailurePhase.ResolveDestination);
        session.ResolutionFailed(error);

        var failed = (SessionState.Failed)session.State;
        await Assert.That(failed.Error).IsEqualTo(error);
    }

    [Test]
    public async Task 起動失敗はFailedになる()
    {
        var session = Session();
        session.RequestConnect();
        session.PlanReady(TestData.Plan());

        session.StartFailed(TestData.Error(FailurePhase.StartSession));

        await Assert.That(session.State).IsTypeOf<SessionState.Failed>();
    }

    [Test]
    public async Task FailedからRequestConnectで再度接続できる()
    {
        var session = Session();
        session.RequestConnect();
        session.ResolutionFailed(TestData.Error());

        session.RequestConnect();

        await Assert.That(session.State).IsTypeOf<SessionState.Resolving>();
    }

    [Test]
    public async Task 自動再接続無効なら予期せぬ終了はFailedになる()
    {
        var session = EstablishedSession(autoReconnect: false);

        session.ExitedUnexpectedly(TestData.Error());

        await Assert.That(session.State).IsTypeOf<SessionState.Failed>();
    }

    [Test]
    public async Task 自動再接続有効なら予期せぬ終了はReconnectingになる()
    {
        var session = EstablishedSession(autoReconnect: true);

        var cause = TestData.Error();
        session.ExitedUnexpectedly(cause);

        var reconnecting = (SessionState.Reconnecting)session.State;
        await Assert.That(reconnecting.Attempt).IsEqualTo(1);
        await Assert.That(reconnecting.Delay).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(reconnecting.Cause).IsEqualTo(cause);
    }

    [Test]
    public async Task 再接続サイクル中の失敗はバックオフしながら上限まで再試行する()
    {
        var session = EstablishedSession(autoReconnect: true);
        session.ExitedUnexpectedly(TestData.Error());

        var expectedDelays = new[] { 4, 8, 16, 30 };
        foreach (var expected in expectedDelays)
        {
            session.RetryDue();
            await Assert.That(session.State).IsTypeOf<SessionState.Resolving>();

            session.ResolutionFailed(TestData.Error());
            var reconnecting = (SessionState.Reconnecting)session.State;
            await Assert.That(reconnecting.Delay).IsEqualTo(TimeSpan.FromSeconds(expected));
        }

        // 5 回目 (上限) の試行が失敗すると Failed
        session.RetryDue();
        session.ResolutionFailed(TestData.Error());
        await Assert.That(session.State).IsTypeOf<SessionState.Failed>();
    }

    [Test]
    public async Task 再接続に成功すると試行回数はリセットされる()
    {
        var session = EstablishedSession(autoReconnect: true);
        session.ExitedUnexpectedly(TestData.Error());
        session.RetryDue();
        session.PlanReady(TestData.Plan());
        session.MarkEstablished(DateTimeOffset.UnixEpoch);

        await Assert.That(session.ReconnectAttempt).IsEqualTo(0);

        // 次の切断はまた 1 回目から
        session.ExitedUnexpectedly(TestData.Error());
        var reconnecting = (SessionState.Reconnecting)session.State;
        await Assert.That(reconnecting.Attempt).IsEqualTo(1);
    }

    [Test]
    public async Task 再試行待ち中の切断要求はIdleに戻る()
    {
        var session = EstablishedSession(autoReconnect: true);
        session.ExitedUnexpectedly(TestData.Error());

        session.RequestDisconnect();

        await Assert.That(session.State).IsTypeOf<SessionState.Idle>();
        await Assert.That(session.ReconnectAttempt).IsEqualTo(0);
    }

    [Test]
    public async Task 手動接続の失敗は自動再接続の対象にしない()
    {
        var session = Session(autoReconnect: true);
        session.RequestConnect();

        session.ResolutionFailed(TestData.Error());

        await Assert.That(session.State).IsTypeOf<SessionState.Failed>();
    }

    [Test]
    public async Task 接続処理中の切断要求はキャンセルとしてClosingになる()
    {
        var session = Session();
        session.RequestConnect();

        session.RequestDisconnect();

        await Assert.That(session.State).IsTypeOf<SessionState.Closing>();
    }

    [Test]
    public async Task 不正な遷移は例外を投げる()
    {
        await Assert.That(() => Session().PlanReady(TestData.Plan()))
            .Throws<InvalidSessionTransitionException>();
        await Assert.That(() => Session().MarkEstablished(DateTimeOffset.UnixEpoch))
            .Throws<InvalidSessionTransitionException>();
        await Assert.That(() => Session().RequestDisconnect())
            .Throws<InvalidSessionTransitionException>();
        await Assert.That(() => Session().MarkClosed())
            .Throws<InvalidSessionTransitionException>();
        await Assert.That(() => EstablishedSession().RequestConnect())
            .Throws<InvalidSessionTransitionException>();
    }
}
