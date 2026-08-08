using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shionji.Application;
using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Presentation;

/// <summary>状態ドットの表示種別。色へのマッピングはビュー側で行う。</summary>
public enum StatusKind
{
    /// <summary>未接続 (灰)。</summary>
    NotConnected,

    /// <summary>解決中 / 接続中 / 再接続待ち (青)。</summary>
    Busy,

    /// <summary>確立 (緑)。</summary>
    Connected,

    /// <summary>失敗 (赤)。</summary>
    Failed,
}

/// <summary>一覧の 1 行。1 行目 = 設定名 + 状態、2 行目 = ローカルポートと転送先の要約。</summary>
public sealed partial class ConfigRowViewModel(MainViewModel owner, ConfigId configId) : ObservableObject
{
    public ConfigId ConfigId { get; } = configId;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial StatusKind Status { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>接続トグルの ON 状態 (接続処理中も ON)。</summary>
    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [RelayCommand]
    private Task ToggleConnectionAsync() => owner.ToggleConnectionAsync(ConfigId);

    internal void Update(
        ForwardingConfig config,
        SessionState state,
        ConfigResolutionView? view,
        Port? actualLocalPort)
    {
        Name = config.Name.Value;
        Status = StatusOf(state);
        IsConnected = state is SessionState.Resolving
            or SessionState.Starting
            or SessionState.Established
            or SessionState.Reconnecting;
        Summary = BuildSummary(config, view, actualLocalPort);
    }

    public static StatusKind StatusOf(SessionState state) => state switch
    {
        SessionState.Idle => StatusKind.NotConnected,
        SessionState.Established => StatusKind.Connected,
        SessionState.Failed => StatusKind.Failed,
        _ => StatusKind.Busy,
    };

    /// <summary>「:13306 → prod-aurora….rds.amazonaws.com:3306」形式の要約 (純粋関数)。</summary>
    public static string BuildSummary(
        ForwardingConfig config,
        ConfigResolutionView? view,
        Port? actualLocalPort)
    {
        var local = config.LocalPort switch
        {
            LocalPortSpec.Fixed fixedPort => fixedPort.Port.Value.ToString(),
            _ => actualLocalPort?.Value.ToString() ?? "auto",
        };

        return $":{local} → {DestinationSummary(config, view)}";
    }

    /// <summary>転送先の要約。一覧の 2 行目と詳細ペインの「転送先」で共用する。</summary>
    public static string DestinationSummary(ForwardingConfig config, ConfigResolutionView? view)
    {
        switch (config.Destination)
        {
            case Destination.Static s:
                return $"{s.Host.Value}:{s.Port.Value}";

            case Destination.Query query:
                switch (view?.Destination)
                {
                    case ResolutionOutcome.Resolved resolved:
                        var direct = config.Gateway is GatewaySpec.Direct;
                        if (direct)
                            return $"{resolved.Resource.DisplayName} (direct)";
                        var host = resolved.Resource.Host?.Value ?? resolved.Resource.DisplayName;
                        var port = query.Port switch
                        {
                            PortSelection.Explicit explicitPort => explicitPort.Port.Value,
                            _ => resolved.Resource.DefaultPort?.Value,
                        };
                        return port is { } p ? $"{host}:{p}" : host;

                    case ResolutionOutcome.Ambiguous ambiguous:
                        return $"複数一致 ({ambiguous.Candidates.Count} 件)";

                    case ResolutionOutcome.NotFound:
                        return "見つかりません";

                    case ResolutionOutcome.Failed:
                        return "検索エラー";

                    default:
                        return view is { IsResolving: true } ? "自動検索中…" : "未検索";
                }

            default:
                return string.Empty;
        }
    }
}
