using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shionji.Application;
using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;

namespace Shionji.Presentation;

/// <summary>右ペインの詳細表示。概要 / 解決結果 (候補・エラー含む) / セッション状態 / 操作。</summary>
public sealed partial class ConfigDetailViewModel(
    MainViewModel owner,
    Domain.ValueObjects.ConfigId configId,
    IClipboardService clipboard) : ObservableObject
{
    private const int MaxLogLines = 200;

    public Domain.ValueObjects.ConfigId ConfigId { get; } = configId;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Overview { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DestinationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GatewayText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SessionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    /// <summary>「localhost:13306」。確立時のみ。</summary>
    [ObservableProperty]
    public partial string? LocalEndpoint { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial StatusKind Status { get; set; }

    /// <summary>資格情報エラー中で、アプリ内 SSO ログインを提示できる。</summary>
    [ObservableProperty]
    public partial bool CanSsoLogin { get; set; }

    [ObservableProperty]
    public partial bool IsLoggingIn { get; set; }

    /// <summary>Ambiguous 時の候補一覧。</summary>
    public ObservableCollection<string> Candidates { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    [RelayCommand]
    private Task ToggleConnectionAsync() => owner.ToggleConnectionAsync(ConfigId);

    [RelayCommand]
    private Task RefreshResolutionAsync() => owner.RefreshResolutionAsync(ConfigId);

    [RelayCommand]
    private void CopyLocalEndpoint()
    {
        if (LocalEndpoint is { } endpoint)
            clipboard.SetText(endpoint);
    }

    [RelayCommand]
    private void Edit() => owner.ShowEditor(ConfigId);

    /// <summary>ブラウザ承認込みの SSO ログイン。完了後は再解決 / 再接続される。</summary>
    [RelayCommand]
    private async Task SsoLoginAsync()
    {
        IsLoggingIn = true;
        try
        {
            if (await owner.SsoLoginAsync(ConfigId) is { } error)
                ErrorText = $"[{PhaseLabel(error.Phase)}] {error.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteConfigAsync(ConfigId);

    internal void AppendLog(SessionLogEventArgs log)
    {
        if (log.ConfigId != ConfigId)
            return;

        LogLines.Add(log.IsError ? $"[stderr] {log.Line}" : log.Line);
        while (LogLines.Count > MaxLogLines)
            LogLines.RemoveAt(0);
    }

    internal void Refresh(
        ForwardingConfig config,
        SessionState state,
        ConfigResolutionView? view,
        Domain.ValueObjects.Port? localPort)
    {
        Name = config.Name.Value;
        Overview = $"プロファイル {config.Aws.Profile.Value} / {config.Aws.Region.Value}";
        GatewayText = GatewaySummary(config.Gateway);
        DestinationText = ConfigRowViewModel.BuildSummary(config, view, localPort);
        Status = ConfigRowViewModel.StatusOf(state);
        IsConnected = state is SessionState.Resolving
            or SessionState.Starting
            or SessionState.Established
            or SessionState.Reconnecting;
        SessionText = SessionSummary(state);
        LocalEndpoint = state is SessionState.Established established
            ? $"localhost:{established.Plan.LocalPort.Value}"
            : null;
        ErrorText = ComposeError(state, view);
        CanSsoLogin = HasCredentialsError(state, view);

        Candidates.Clear();
        foreach (var candidate in CollectCandidates(view))
            Candidates.Add(candidate);
    }

    public static string GatewaySummary(GatewaySpec gateway) => gateway switch
    {
        GatewaySpec.Direct => "直接 (転送先に SSM セッション)",
        GatewaySpec.Ec2 { Selector: Ec2Selector.ById byId } => $"EC2 踏み台 {byId.Id.Value}",
        GatewaySpec.Ec2 { Selector: Ec2Selector.ByQuery byQuery } =>
            $"EC2 踏み台 (検索: {byQuery.Query.Name?.Value ?? "*"})",
        GatewaySpec.Ecs ecs => $"ECS 踏み台 {ecs.Cluster.Value}/{ecs.Service.Value}",
        _ => string.Empty,
    };

    private static string SessionSummary(SessionState state) => state switch
    {
        SessionState.Idle => "未接続",
        SessionState.Resolving => "リソース解決中…",
        SessionState.Starting => "セッション起動中…",
        SessionState.Established established => $"確立 ({established.Since:HH:mm:ss} から)",
        SessionState.Closing => "切断中…",
        SessionState.Reconnecting reconnecting =>
            $"再接続待ち ({reconnecting.Attempt} 回目、{reconnecting.Delay.TotalSeconds:0} 秒後)",
        SessionState.Failed => "失敗",
        _ => string.Empty,
    };

    private static string? ComposeError(SessionState state, ConfigResolutionView? view)
    {
        if (state is SessionState.Failed failed)
            return $"[{PhaseLabel(failed.Error.Phase)}] {failed.Error.Message}";
        if (state is SessionState.Reconnecting reconnecting)
            return $"[{PhaseLabel(reconnecting.Cause.Phase)}] {reconnecting.Cause.Message}";

        // 未接続でも解決結果側の問題は表示する
        foreach (var (outcome, label) in new[] { (view?.Destination, "転送先"), (view?.Gateway, "踏み台") })
        {
            switch (outcome)
            {
                case ResolutionOutcome.Failed resolutionFailed:
                    return $"[{label}] {resolutionFailed.Error.Message}";
                case ResolutionOutcome.NotFound:
                    return $"[{label}] 条件に一致するリソースが見つかりません。";
                case ResolutionOutcome.Ambiguous ambiguous:
                    return $"[{label}] {ambiguous.Candidates.Count} 件が一致しました。条件を絞り込んでください。";
            }
        }

        return null;
    }

    private static bool HasCredentialsError(SessionState state, ConfigResolutionView? view) =>
        state is SessionState.Failed { Error.Phase: FailurePhase.Credentials }
        || state is SessionState.Reconnecting { Cause.Phase: FailurePhase.Credentials }
        || view?.Destination is ResolutionOutcome.Failed { Error.Phase: FailurePhase.Credentials }
        || view?.Gateway is ResolutionOutcome.Failed { Error.Phase: FailurePhase.Credentials };

    public static string PhaseLabel(FailurePhase phase) => phase switch
    {
        FailurePhase.Credentials => "認証",
        FailurePhase.Permission => "権限",
        FailurePhase.ResolveDestination => "転送先の解決",
        FailurePhase.ResolveGateway => "踏み台の解決",
        FailurePhase.StartSession => "セッション開始",
        FailurePhase.Plugin => "plugin",
        _ => phase.ToString(),
    };

    private static IEnumerable<string> CollectCandidates(ConfigResolutionView? view)
    {
        foreach (var outcome in new[] { view?.Destination, view?.Gateway })
        {
            if (outcome is not ResolutionOutcome.Ambiguous ambiguous)
                continue;
            foreach (var candidate in ambiguous.Candidates)
            {
                yield return candidate.Host is { } host
                    ? $"{candidate.DisplayName} ({host.Value})"
                    : candidate.DisplayName;
            }
        }
    }
}
