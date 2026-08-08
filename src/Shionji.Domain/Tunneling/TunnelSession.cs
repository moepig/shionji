using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tunneling;

/// <summary>現在の状態では受け付けられない操作が要求された。</summary>
public sealed class InvalidSessionTransitionException(string operation, SessionState state)
    : InvalidOperationException($"状態 {state.GetType().Name} では操作「{operation}」を受け付けられません。")
{
    public string Operation { get; } = operation;
    public SessionState State { get; } = state;
}

/// <summary>
/// 1 転送設定の接続ライフサイクルを表す純粋な状態機械 (実行時エンティティ、永続化しない)。
/// プロセスや API の実操作はアプリケーション層が行い、その結果をイベントとして本エンティティに適用する。
/// </summary>
public sealed class TunnelSession
{
    public ConfigId ConfigId { get; }
    public bool AutoReconnect { get; }
    public ReconnectPolicy Policy { get; }

    public SessionState State { get; private set; } = SessionState.Idle.Instance;

    /// <summary>現在の再接続サイクルの試行回数。通常接続中は 0。</summary>
    public int ReconnectAttempt { get; private set; }

    public TunnelSession(ConfigId configId, bool autoReconnect, ReconnectPolicy? policy = null)
    {
        ConfigId = configId;
        AutoReconnect = autoReconnect;
        Policy = policy ?? ReconnectPolicy.Default;
    }

    /// <summary>接続要求。Idle / Failed から解決を開始する。</summary>
    public void RequestConnect()
    {
        if (State is not (SessionState.Idle or SessionState.Failed))
            throw new InvalidSessionTransitionException("接続要求", State);

        ReconnectAttempt = 0;
        State = SessionState.Resolving.Instance;
    }

    /// <summary>解決が完了しトンネル計画が確定した。</summary>
    public void PlanReady(TunnelPlan plan)
    {
        if (State is not SessionState.Resolving)
            throw new InvalidSessionTransitionException("計画確定", State);

        State = new SessionState.Starting(plan);
    }

    /// <summary>リソース解決に失敗した。</summary>
    public void ResolutionFailed(ErrorDetail error)
    {
        if (State is not SessionState.Resolving)
            throw new InvalidSessionTransitionException("解決失敗", State);

        FailOrScheduleReconnect(error);
    }

    /// <summary>plugin がローカルポートを開き、トンネルが確立した。</summary>
    public void MarkEstablished(DateTimeOffset now)
    {
        if (State is not SessionState.Starting starting)
            throw new InvalidSessionTransitionException("確立", State);

        ReconnectAttempt = 0;
        State = new SessionState.Established(starting.Plan, now);
    }

    /// <summary>StartSession / plugin の起動に失敗した。</summary>
    public void StartFailed(ErrorDetail error)
    {
        if (State is not SessionState.Starting)
            throw new InvalidSessionTransitionException("起動失敗", State);

        FailOrScheduleReconnect(error);
    }

    /// <summary>確立済みトンネルが予期せず終了した。</summary>
    public void ExitedUnexpectedly(ErrorDetail error)
    {
        if (State is not SessionState.Established)
            throw new InvalidSessionTransitionException("予期せぬ終了", State);

        if (AutoReconnect && Policy.MaxAttempts >= 1)
        {
            ReconnectAttempt = 1;
            State = new SessionState.Reconnecting(1, Policy.DelayFor(1), error);
        }
        else
        {
            State = new SessionState.Failed(error);
        }
    }

    /// <summary>再試行待機が満了し、解決からやり直す。</summary>
    public void RetryDue()
    {
        if (State is not SessionState.Reconnecting)
            throw new InvalidSessionTransitionException("再試行", State);

        State = SessionState.Resolving.Instance;
    }

    /// <summary>切断要求。接続処理中はキャンセルとして扱う。再試行待ちは即座に Idle へ戻る。</summary>
    public void RequestDisconnect()
    {
        switch (State)
        {
            case SessionState.Resolving or SessionState.Starting or SessionState.Established:
                State = SessionState.Closing.Instance;
                break;
            case SessionState.Reconnecting:
                ReconnectAttempt = 0;
                State = SessionState.Idle.Instance;
                break;
            default:
                throw new InvalidSessionTransitionException("切断要求", State);
        }
    }

    /// <summary>切断処理が完了した。</summary>
    public void MarkClosed()
    {
        if (State is not SessionState.Closing)
            throw new InvalidSessionTransitionException("切断完了", State);

        ReconnectAttempt = 0;
        State = SessionState.Idle.Instance;
    }

    /// <summary>
    /// 再接続サイクル中 (ReconnectAttempt ≧ 1) の失敗は上限まで次の再試行を予約し、
    /// 手動接続の失敗と上限到達は Failed とする。
    /// </summary>
    private void FailOrScheduleReconnect(ErrorDetail error)
    {
        if (AutoReconnect && ReconnectAttempt >= 1 && ReconnectAttempt < Policy.MaxAttempts)
        {
            ReconnectAttempt++;
            State = new SessionState.Reconnecting(ReconnectAttempt, Policy.DelayFor(ReconnectAttempt), error);
        }
        else
        {
            ReconnectAttempt = 0;
            State = new SessionState.Failed(error);
        }
    }
}
