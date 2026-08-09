using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Application;

/// <summary>切断がどの操作によって起きたか。監査ログに残す。</summary>
public enum StopReason
{
    /// <summary>利用者が切断を指示した。</summary>
    UserRequest,

    /// <summary>設定の保存 / 削除に伴う切断。</summary>
    ConfigChanged,

    /// <summary>アプリ終了に伴う切断。</summary>
    ApplicationExit,
}

public sealed class SessionChangedEventArgs(ConfigId configId, SessionState state, Port? localPort) : EventArgs
{
    public ConfigId ConfigId { get; } = configId;
    public SessionState State { get; } = state;

    /// <summary>確立中のトンネルが待ち受けているローカルポート。未確立なら null。</summary>
    public Port? LocalPort { get; } = localPort;
}

public sealed class SessionLogEventArgs(ConfigId configId, string line, bool isError) : EventArgs
{
    public ConfigId ConfigId { get; } = configId;
    public string Line { get; } = line;
    public bool IsError { get; } = isError;
}

/// <summary>
/// 全トンネルセッションの監督。設定 1 件につき同時セッション 1 本を保証し、
/// 接続開始時は再解決してから計画を作り、予期せぬ終了時は方針に従い自動再接続する。
/// </summary>
public sealed class TunnelSupervisor(
    IResourceCatalog catalog,
    ITunnelLauncher launcher,
    ILocalPortProbe portProbe,
    IClock clock,
    IRetryScheduler retryScheduler,
    ResolutionService resolutionService,
    ILogger<TunnelSupervisor>? logger = null) : IAsyncDisposable
{
    private readonly ILogger _log = logger ?? NullLogger<TunnelSupervisor>.Instance;

    private sealed class SessionContext(ForwardingConfig config)
    {
        public ForwardingConfig Config { get; set; } = config;
        public TunnelSession Session { get; set; } = new(config.Id, config.Options.AutoReconnect);
        public ITunnelHandle? Handle { get; set; }
        public CancellationTokenSource? OpCts { get; set; }

        /// <summary>接続試行ごとの相関 ID。1 回の試行に属するログ行を突き合わせるために使う。</summary>
        public string AttemptId { get; set; } = string.Empty;

        /// <summary>切断時に接続時間を算出するための確立時刻。</summary>
        public DateTimeOffset? EstablishedAt { get; set; }

        /// <summary>Start / Stop のたびに進む世代番号。旧世代の進行中処理は状態を変更しない。</summary>
        public int Epoch { get; set; }
    }

    private readonly object _sync = new();
    private readonly Dictionary<ConfigId, SessionContext> _sessions = [];

    public event EventHandler<SessionChangedEventArgs>? SessionChanged;
    public event EventHandler<SessionLogEventArgs>? SessionLog;

    public SessionState GetState(ConfigId id)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(id, out var ctx) ? ctx.Session.State : SessionState.Idle.Instance;
        }
    }

    public Port? GetLocalPort(ConfigId id) =>
        GetState(id) is SessionState.Established established ? established.Plan.LocalPort : null;

    /// <summary>接続を開始する。既に接続処理中 / 接続済みの設定に対しては何もしない。</summary>
    public async Task StartAsync(ForwardingConfig config, CancellationToken cancellationToken = default)
    {
        SessionContext ctx;
        int epoch;
        CancellationToken opToken;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(config.Id, out ctx!))
            {
                ctx = new SessionContext(config);
                _sessions[config.Id] = ctx;
            }

            if (ctx.Session.State is not (SessionState.Idle or SessionState.Failed))
                return;

            // 設定編集後の最新値と AutoReconnect を反映するため、接続のたびにセッションを作り直す
            ctx.Config = config;
            ctx.Session = new TunnelSession(config.Id, config.Options.AutoReconnect);
            ctx.Session.RequestConnect();
            ctx.Epoch++;
            ctx.AttemptId = Guid.NewGuid().ToString("N")[..8];
            ctx.EstablishedAt = null;
            epoch = ctx.Epoch;
            ctx.OpCts?.Dispose();
            ctx.OpCts = new CancellationTokenSource();
            opToken = ctx.OpCts.Token;
        }

        Emit(ctx);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, opToken);
        await RunSessionAsync(ctx, epoch, linked.Token);
    }

    public async Task StopAsync(ConfigId id, StopReason reason = StopReason.UserRequest)
    {
        SessionContext? ctx;
        ITunnelHandle? handle = null;
        var closing = false;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(id, out ctx))
                return;

            LogStopRequested(ctx, reason);
            switch (ctx.Session.State)
            {
                case SessionState.Resolving or SessionState.Starting or SessionState.Established:
                    ctx.Epoch++;
                    ctx.OpCts?.Cancel();
                    ctx.Session.RequestDisconnect();
                    handle = ctx.Handle;
                    ctx.Handle = null;
                    closing = true;
                    break;

                case SessionState.Reconnecting:
                    ctx.Epoch++;
                    ctx.OpCts?.Cancel();
                    ctx.Session.RequestDisconnect();
                    break;

                default:
                    return;
            }
        }

        Emit(ctx);

        if (closing)
        {
            if (handle is not null)
            {
                try
                {
                    await handle.StopAsync();
                }
                finally
                {
                    await handle.DisposeAsync();
                }
            }

            lock (_sync)
            {
                ctx.Session.MarkClosed();
            }

            Emit(ctx);
        }
    }

    public async Task StopAllAsync(StopReason reason = StopReason.ApplicationExit)
    {
        ConfigId[] ids;
        lock (_sync)
        {
            ids = [.. _sessions.Keys];
        }

        foreach (var id in ids)
            await StopAsync(id, reason);
    }

    public ValueTask DisposeAsync() => new(StopAllAsync());

    /// <summary>切断がどの操作によるものかと、接続していた時間を記録する。</summary>
    private void LogStopRequested(SessionContext ctx, StopReason reason)
    {
        if (ctx.Session.State is not (SessionState.Resolving or SessionState.Starting
            or SessionState.Established or SessionState.Reconnecting))
        {
            return;
        }

        _log.Audit(LogLevel.Information, $"[設定名: {ctx.Config.Name.Value}] 切断します ({ReasonLabel(reason)})",
            ("attempt", ctx.AttemptId),
            ("config", ctx.Config.Name.Value),
            ("reason", ReasonLabel(reason)),
            ("session", ctx.Handle?.SessionId),
            ("connectedSeconds", ctx.EstablishedAt is { } since
                ? (long)(clock.UtcNow - since).TotalSeconds
                : null));
    }

    private static string ReasonLabel(StopReason reason) => reason switch
    {
        StopReason.UserRequest => "利用者操作",
        StopReason.ConfigChanged => "設定変更",
        StopReason.ApplicationExit => "アプリ終了",
        _ => reason.ToString(),
    };

    /// <summary>
    /// 接続試行と、失敗時の再接続サイクル (バックオフ待機 → 再試行) を回す。
    /// 予期しない例外はセッションを Failed へ落として封じ込める
    /// (fire-and-forget で走るため、漏らすと Reconnecting のまま固まって原因も残らない)。
    /// </summary>
    private async Task RunSessionAsync(SessionContext ctx, int epoch, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    await AttemptOnceAsync(ctx, epoch, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                SessionState state;
                lock (_sync)
                {
                    if (ctx.Epoch != epoch)
                        return;
                    state = ctx.Session.State;
                }

                if (state is not SessionState.Reconnecting reconnecting)
                    return;

                try
                {
                    await retryScheduler.DelayAsync(reconnecting.Delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                lock (_sync)
                {
                    if (ctx.Epoch != epoch || ctx.Session.State is not SessionState.Reconnecting)
                        return;
                    // 再試行は別の試行として追えるよう相関 ID を振り直す
                    ctx.AttemptId = Guid.NewGuid().ToString("N")[..8];
                    ctx.Session.RetryDue();
                }

                Emit(ctx);
            }
        }
        catch (Exception ex)
        {
            AbortOnUnexpectedFailure(ctx, epoch, ex);
        }
    }

    private void AbortOnUnexpectedFailure(SessionContext ctx, int epoch, Exception exception)
    {
        _log.LogError(exception, "{Config}: 内部エラーが発生しました 試行={Attempt}", ctx.Config.Name.Value, ctx.AttemptId);
        lock (_sync)
        {
            if (ctx.Epoch != epoch)
                return;
            ctx.Session.Abort(new ErrorDetail(
                FailurePhase.Plugin, "InternalError", $"内部エラーが発生しました: {exception.Message}"));
        }

        Emit(ctx);
    }

    /// <summary>解決 → ローカルポート確定 → 計画 → 起動の 1 回分。</summary>
    private async Task AttemptOnceAsync(SessionContext ctx, int epoch, CancellationToken cancellationToken)
    {
        var config = ctx.Config;

        ResolutionOutcome? destinationOutcome = null;
        ResolvedResource? destinationResource = null;
        if (config.Destination is Destination.Query query)
        {
            destinationOutcome = await SafeResolver.ResolveAsync(
                catalog, config.Aws, query.ResourceQuery, FailurePhase.ResolveDestination, cancellationToken);
            if (OutcomeErrors.ToErrorDetail(destinationOutcome, FailurePhase.ResolveDestination) is { } error)
            {
                resolutionService.Publish(config, destinationOutcome, null);
                ApplyResolutionFailure(ctx, epoch, error);
                return;
            }

            destinationResource = ((ResolutionOutcome.Resolved)destinationOutcome).Resource;
        }

        ResolutionOutcome? gatewayOutcome = null;
        ResolvedResource? gatewayResource = null;
        if (GatewayQueries.QueryFor(config.Gateway) is { } gatewayQuery)
        {
            gatewayOutcome = await SafeResolver.ResolveAsync(
                catalog, config.Aws, gatewayQuery, FailurePhase.ResolveGateway, cancellationToken);
            if (OutcomeErrors.ToErrorDetail(gatewayOutcome, FailurePhase.ResolveGateway) is { } error)
            {
                resolutionService.Publish(config, destinationOutcome, gatewayOutcome);
                ApplyResolutionFailure(ctx, epoch, error);
                return;
            }

            gatewayResource = ((ResolutionOutcome.Resolved)gatewayOutcome).Resource;
        }

        if (destinationOutcome is not null || gatewayOutcome is not null)
        {
            resolutionService.Publish(config, destinationOutcome, gatewayOutcome);

            // どの検索条件がどの実リソースに解決されたかを証跡として残す
            _log.Audit(LogLevel.Information, $"[設定名: {config.Name.Value}] リソースを特定しました",
                [("attempt", ctx.AttemptId), ("config", config.Name.Value),
                 .. ResolvedDetails("destination", destinationResource),
                 .. ResolvedDetails("bastion", gatewayResource)]);
        }

        Port localPort;
        switch (config.LocalPort)
        {
            case LocalPortSpec.Fixed fixedPort:
                if (!portProbe.IsAvailable(fixedPort.Port))
                {
                    ApplyResolutionFailure(ctx, epoch, new ErrorDetail(
                        FailurePhase.StartSession, "LocalPortInUse", $"ローカルポート {fixedPort.Port} は使用中です。"));
                    return;
                }

                localPort = fixedPort.Port;
                break;

            case LocalPortSpec.Auto:
                var acquired = portProbe.AcquireFreePort();
                if (acquired.IsFailure)
                {
                    ApplyResolutionFailure(ctx, epoch, acquired.Error);
                    return;
                }

                localPort = acquired.Value;
                break;

            default:
                throw new InvalidOperationException($"未知のローカルポート指定: {config.LocalPort.GetType()}");
        }

        var planResult = TunnelPlanner.CreatePlan(config, destinationResource, gatewayResource, localPort);
        if (planResult.IsFailure)
        {
            ApplyResolutionFailure(ctx, epoch, planResult.Error);
            return;
        }

        var plan = planResult.Value;
        lock (_sync)
        {
            if (ctx.Epoch != epoch)
                return;
            ctx.Session.PlanReady(plan);
        }

        Emit(ctx);

        var launched = await LaunchSafelyAsync(plan, cancellationToken);
        if (launched.IsFailure)
        {
            lock (_sync)
            {
                if (ctx.Epoch != epoch)
                    return;
                ctx.Session.StartFailed(launched.Error);
            }

            Emit(ctx);
            return;
        }

        var handle = launched.Value;
        var stopped = false;
        lock (_sync)
        {
            if (ctx.Epoch != epoch)
            {
                stopped = true;
            }
            else
            {
                ctx.Handle = handle;
                ctx.EstablishedAt = clock.UtcNow;
                handle.Exited += (_, e) => OnTunnelExited(ctx, epoch, e.Error);
                handle.LogEmitted += (_, e) =>
                    SessionLog?.Invoke(this, new SessionLogEventArgs(config.Id, e.Line, e.IsError));
                ctx.Session.MarkEstablished(clock.UtcNow);
            }
        }

        if (stopped)
        {
            // 起動中に停止要求が入っていた。起動してしまったトンネルは畳む
            try
            {
                await handle.StopAsync(CancellationToken.None);
            }
            finally
            {
                await handle.DisposeAsync();
            }

            return;
        }

        Emit(ctx);
    }

    private void OnTunnelExited(SessionContext ctx, int epoch, ErrorDetail error)
    {
        try
        {
            ITunnelHandle? handle;
            CancellationToken opToken;
            lock (_sync)
            {
                if (ctx.Epoch != epoch || ctx.Session.State is not SessionState.Established)
                    return;

                handle = ctx.Handle;
                _log.Audit(LogLevel.Warning, $"[設定名: {ctx.Config.Name.Value}] 接続が切れました",
                    ("attempt", ctx.AttemptId),
                    ("config", ctx.Config.Name.Value),
                    ("session", handle?.SessionId),
                    ("phase", error.Phase),
                    ("code", error.Code),
                    ("cause", error.Message),
                    ("connectedSeconds", ctx.EstablishedAt is { } since
                        ? (long)(clock.UtcNow - since).TotalSeconds
                        : null));

                ctx.Handle = null;
                ctx.EstablishedAt = null;
                ctx.Session.ExitedUnexpectedly(error);
                opToken = ctx.OpCts?.Token ?? CancellationToken.None;
            }

            if (handle is not null)
                _ = handle.DisposeAsync();

            Emit(ctx);

            SessionState state;
            lock (_sync)
            {
                state = ctx.Session.State;
            }

            if (state is SessionState.Reconnecting reconnecting)
                _ = ReconnectLoopAsync(ctx, epoch, reconnecting.Delay, opToken);
        }
        catch (Exception ex)
        {
            AbortOnUnexpectedFailure(ctx, epoch, ex);
        }
    }

    private async Task ReconnectLoopAsync(
        SessionContext ctx, int epoch, TimeSpan initialDelay, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await retryScheduler.DelayAsync(initialDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_sync)
            {
                if (ctx.Epoch != epoch || ctx.Session.State is not SessionState.Reconnecting)
                    return;
                ctx.AttemptId = Guid.NewGuid().ToString("N")[..8];
                ctx.Session.RetryDue();
            }

            Emit(ctx);
        }
        catch (Exception ex)
        {
            AbortOnUnexpectedFailure(ctx, epoch, ex);
            return;
        }

        await RunSessionAsync(ctx, epoch, cancellationToken);
    }

    private void ApplyResolutionFailure(SessionContext ctx, int epoch, ErrorDetail error)
    {
        lock (_sync)
        {
            if (ctx.Epoch != epoch)
                return;
            ctx.Session.ResolutionFailed(error);
        }

        Emit(ctx);
    }

    private async Task<Result<ITunnelHandle, ErrorDetail>> LaunchSafelyAsync(
        TunnelPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            return await launcher.LaunchAsync(plan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<ITunnelHandle, ErrorDetail>.Failure(
                new ErrorDetail(FailurePhase.StartSession, "Unexpected", ex.Message));
        }
    }

    /// <summary>
    /// 状態遷移を記録する。要約は画面のステータスバーへ、
    /// 詳細フィールド (どの経路でどこへ繋いだか) はファイルログへ展開される。
    /// </summary>
    private void LogStateChange(SessionContext ctx, SessionState state)
    {
        var name = ctx.Config.Name.Value;
        var aws = $"{ctx.Config.Aws.Profile.Value}@{ctx.Config.Aws.Region.Value}";

        switch (state)
        {
            case SessionState.Resolving:
                _log.Audit(LogLevel.Information, $"[設定名: {name}] リソースを自動検索しています…",
                    ("attempt", ctx.AttemptId), ("config", name), ("profile", aws));
                break;

            case SessionState.Starting starting:
                _log.Audit(LogLevel.Information, $"[設定名: {name}] セッションを開始しています…",
                    PlanDetails(ctx, starting.Plan));
                break;

            case SessionState.Established established:
                _log.Audit(LogLevel.Information,
                    $"[設定名: {name}] localhost:{established.Plan.LocalPort.Value} で接続しました",
                    [.. PlanDetails(ctx, established.Plan), ("session", ctx.Handle?.SessionId)]);
                break;

            case SessionState.Closing:
                _log.Audit(LogLevel.Information, $"[設定名: {name}] 切断しています…",
                    ("attempt", ctx.AttemptId), ("config", name));
                break;

            case SessionState.Idle:
                _log.Audit(LogLevel.Information, $"[設定名: {name}] 切断しました",
                    ("attempt", ctx.AttemptId), ("config", name));
                break;

            case SessionState.Reconnecting reconnecting:
                _log.Audit(LogLevel.Warning,
                    $"[設定名: {name}] 切断されました。{reconnecting.Delay.TotalSeconds:0} 秒後に再接続します ({reconnecting.Attempt} 回目)",
                    ("attempt", ctx.AttemptId),
                    ("config", name),
                    ("retryCount", reconnecting.Attempt),
                    ("delaySeconds", reconnecting.Delay.TotalSeconds),
                    ("phase", reconnecting.Cause.Phase),
                    ("code", reconnecting.Cause.Code),
                    ("cause", reconnecting.Cause.Message));
                break;

            case SessionState.Failed failed:
                _log.Audit(LogLevel.Error, $"[設定名: {name}] 失敗: {failed.Error.Message}",
                    ("attempt", ctx.AttemptId),
                    ("config", name),
                    ("phase", failed.Error.Phase),
                    ("code", failed.Error.Code),
                    ("profile", aws));
                break;
        }
    }

    /// <summary>解決結果の証跡 (表示名だけでなく実リソースの識別子まで)。</summary>
    private static (string, object?)[] ResolvedDetails(string label, ResolvedResource? resource)
    {
        if (resource is null)
            return [];

        var endpoint = resource.Host is { } host
            ? $"{host.Value}{(resource.DefaultPort is { } port ? $":{port.Value}" : string.Empty)}"
            : null;

        return
        [
            (label, resource.DisplayName),
            ($"{label}Id", resource.Id.Value),
            ($"{label}Endpoint", endpoint),
            ($"{label}SsmTarget", resource.SsmTarget?.Value),
        ];
    }

    /// <summary>監査に必要な接続の事実。</summary>
    private static (string, object?)[] PlanDetails(SessionContext ctx, TunnelPlan plan)
    {
        var destination = plan.Mode switch
        {
            SessionMode.RemoteHostForward remote => $"{remote.Host.Value}:{remote.RemotePort.Value}",
            SessionMode.DirectForward direct => $"{plan.Target.Value}:{direct.RemotePort.Value}",
            _ => null,
        };

        var gateway = ctx.Config.Gateway switch
        {
            GatewaySpec.Direct => "直接",
            GatewaySpec.Ec2 => $"EC2:{plan.Target.Value}",
            GatewaySpec.Ecs => $"ECS:{plan.Target.Value}",
            _ => plan.Target.Value,
        };

        return
        [
            ("attempt", ctx.AttemptId),
            ("config", ctx.Config.Name.Value),
            ("destination", destination),
            ("gateway", gateway),
            ("ssmTarget", plan.Target.Value),
            ("document", plan.Mode.DocumentName),
            ("profile", $"{plan.Aws.Profile.Value}@{plan.Aws.Region.Value}"),
            ("local", $"localhost:{plan.LocalPort.Value}"),
        ];
    }

    private void Emit(SessionContext ctx)
    {
        SessionState state;
        Port? localPort;
        lock (_sync)
        {
            state = ctx.Session.State;
            localPort = state is SessionState.Established established ? established.Plan.LocalPort : null;
        }

        LogStateChange(ctx, state);
        SessionChanged?.Invoke(this, new SessionChangedEventArgs(ctx.Config.Id, state, localPort));
    }
}
