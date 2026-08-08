using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Application;

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
    ResolutionService resolutionService) : IAsyncDisposable
{
    private sealed class SessionContext(ForwardingConfig config)
    {
        public ForwardingConfig Config { get; set; } = config;
        public TunnelSession Session { get; set; } = new(config.Id, config.Options.AutoReconnect);
        public ITunnelHandle? Handle { get; set; }
        public CancellationTokenSource? OpCts { get; set; }

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
            epoch = ctx.Epoch;
            ctx.OpCts?.Dispose();
            ctx.OpCts = new CancellationTokenSource();
            opToken = ctx.OpCts.Token;
        }

        Emit(ctx);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, opToken);
        await RunSessionAsync(ctx, epoch, linked.Token);
    }

    public async Task StopAsync(ConfigId id)
    {
        SessionContext? ctx;
        ITunnelHandle? handle = null;
        var closing = false;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(id, out ctx))
                return;

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

    public async Task StopAllAsync()
    {
        ConfigId[] ids;
        lock (_sync)
        {
            ids = [.. _sessions.Keys];
        }

        foreach (var id in ids)
            await StopAsync(id);
    }

    public ValueTask DisposeAsync() => new(StopAllAsync());

    /// <summary>接続試行と、失敗時の再接続サイクル (バックオフ待機 → 再試行) を回す。</summary>
    private async Task RunSessionAsync(SessionContext ctx, int epoch, CancellationToken cancellationToken)
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
                ctx.Session.RetryDue();
            }

            Emit(ctx);
        }
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
            resolutionService.Publish(config, destinationOutcome, gatewayOutcome);

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
        ITunnelHandle? handle;
        CancellationToken opToken;
        lock (_sync)
        {
            if (ctx.Epoch != epoch || ctx.Session.State is not SessionState.Established)
                return;

            handle = ctx.Handle;
            ctx.Handle = null;
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

    private async Task ReconnectLoopAsync(
        SessionContext ctx, int epoch, TimeSpan initialDelay, CancellationToken cancellationToken)
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
            ctx.Session.RetryDue();
        }

        Emit(ctx);
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

    private void Emit(SessionContext ctx)
    {
        SessionState state;
        Port? localPort;
        lock (_sync)
        {
            state = ctx.Session.State;
            localPort = state is SessionState.Established established ? established.Plan.LocalPort : null;
        }

        SessionChanged?.Invoke(this, new SessionChangedEventArgs(ctx.Config.Id, state, localPort));
    }
}
