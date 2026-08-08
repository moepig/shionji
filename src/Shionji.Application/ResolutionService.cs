using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Application;

/// <summary>リスト表示用の、1 設定分の解決結果スナップショット。</summary>
/// <param name="Destination">転送先クエリの解決結果。直接指定 (解決不要) の場合は null。</param>
/// <param name="Gateway">踏み台クエリの解決結果。解決不要な経路の場合は null。</param>
public sealed record ConfigResolutionView(
    ConfigId ConfigId,
    bool IsResolving,
    ResolutionOutcome? Destination,
    ResolutionOutcome? Gateway,
    DateTimeOffset? RefreshedAt);

/// <summary>
/// リスト表示用の解決結果キャッシュ。起動時の全件解決、行 / 全体の手動更新を担う。
/// 接続開始時の再解決結果は <see cref="TunnelSupervisor"/> が <see cref="Publish"/> で反映する。
/// 手動更新 (遅い) と接続時 Publish (新しい) が競合した場合、
/// 世代番号で古い書き込みを破棄し last-write-wins による巻き戻りを防ぐ。
/// </summary>
public sealed class ResolutionService(
    IResourceCatalog catalog, IClock clock, ILogger<ResolutionService>? logger = null)
{
    private sealed record Entry(ConfigResolutionView View, long Version);

    private readonly ILogger _log = logger ?? NullLogger<ResolutionService>.Instance;
    private readonly object _sync = new();
    private readonly Dictionary<ConfigId, Entry> _entries = [];
    private long _sequence;

    /// <summary>ビューが更新された設定の ID を通知する。</summary>
    public event EventHandler<ConfigId>? ViewChanged;

    public ConfigResolutionView? GetView(ConfigId id)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(id, out var entry) ? entry.View : null;
        }
    }

    public async Task RefreshAsync(ForwardingConfig config, CancellationToken cancellationToken = default)
    {
        long myVersion;
        lock (_sync)
        {
            var previous = _entries.TryGetValue(config.Id, out var entry) ? entry.View : null;
            myVersion = ++_sequence;
            _entries[config.Id] = new Entry(
                new ConfigResolutionView(
                    config.Id, IsResolving: true, previous?.Destination, previous?.Gateway, previous?.RefreshedAt),
                myVersion);
        }

        ViewChanged?.Invoke(this, config.Id);

        Task<ResolutionOutcome>? destinationTask = null;
        if (config.Destination is Destination.Query query)
        {
            destinationTask = SafeResolver.ResolveAsync(
                catalog, config.Aws, query.ResourceQuery, FailurePhase.ResolveDestination, cancellationToken);
        }

        Task<ResolutionOutcome>? gatewayTask = null;
        if (GatewayQueries.QueryFor(config.Gateway) is { } gatewayQuery)
        {
            gatewayTask = SafeResolver.ResolveAsync(
                catalog, config.Aws, gatewayQuery, FailurePhase.ResolveGateway, cancellationToken);
        }

        var destination = destinationTask is null ? null : await destinationTask;
        var gateway = gatewayTask is null ? null : await gatewayTask;

        lock (_sync)
        {
            // この更新の開始後に別の書き込み (接続時の Publish など) があれば、
            // こちらの方が古い情報なので破棄する
            if (!_entries.TryGetValue(config.Id, out var entry) || entry.Version != myVersion)
                return;

            _entries[config.Id] = new Entry(
                new ConfigResolutionView(config.Id, IsResolving: false, destination, gateway, clock.UtcNow),
                ++_sequence);
        }

        LogOutcome(config, "転送先", destination);
        LogOutcome(config, "踏み台", gateway);
        ViewChanged?.Invoke(this, config.Id);
    }

    private void LogOutcome(ForwardingConfig config, string label, ResolutionOutcome? outcome)
    {
        var name = config.Name.Value;
        var aws = $"{config.Aws.Profile.Value}@{config.Aws.Region.Value}";

        switch (outcome)
        {
            case ResolutionOutcome.Resolved resolved:
                _log.Audit(LogLevel.Information, $"{name}: {label}を {resolved.Resource.DisplayName} に解決しました",
                    ("設定", name),
                    ("種別", label),
                    ("リソース", resolved.Resource.DisplayName),
                    ("リソースID", resolved.Resource.Id.Value),
                    ("エンドポイント", resolved.Resource.Host?.Value),
                    ("既定ポート", resolved.Resource.DefaultPort?.Value),
                    ("SSMターゲット", resolved.Resource.SsmTarget?.Value),
                    ("プロファイル", aws));
                break;

            case ResolutionOutcome.NotFound:
                _log.Audit(LogLevel.Warning, $"{name}: 条件に一致する{label}が見つかりません",
                    ("設定", name), ("種別", label), ("プロファイル", aws));
                break;

            case ResolutionOutcome.Ambiguous ambiguous:
                _log.Audit(LogLevel.Warning,
                    $"{name}: {label}が {ambiguous.Candidates.Count} 件一致しました。条件を絞り込んでください",
                    ("設定", name),
                    ("種別", label),
                    ("候補数", ambiguous.Candidates.Count),
                    ("候補", string.Join(", ", ambiguous.Candidates.Select(c => $"{c.DisplayName}[{c.Id.Value}]"))),
                    ("プロファイル", aws));
                break;

            case ResolutionOutcome.Failed failed:
                _log.Audit(LogLevel.Error, $"{name}: {label}の解決に失敗しました",
                    ("設定", name),
                    ("種別", label),
                    ("フェーズ", failed.Error.Phase),
                    ("コード", failed.Error.Code),
                    ("原因", failed.Error.Message),
                    ("プロファイル", aws));
                break;
        }
    }

    /// <summary>全設定を並列に解決する。</summary>
    public Task RefreshAllAsync(IEnumerable<ForwardingConfig> configs, CancellationToken cancellationToken = default) =>
        Task.WhenAll(configs.Select(c => RefreshAsync(c, cancellationToken)));

    /// <summary>
    /// 接続開始時などに外部で得られた最新の解決結果を反映する。
    /// null の側 (未解決のまま失敗した場合など) は直前の値を保持する。
    /// </summary>
    public void Publish(ForwardingConfig config, ResolutionOutcome? destination, ResolutionOutcome? gateway)
    {
        lock (_sync)
        {
            var previous = _entries.TryGetValue(config.Id, out var entry) ? entry.View : null;
            _entries[config.Id] = new Entry(
                new ConfigResolutionView(
                    config.Id,
                    IsResolving: false,
                    destination ?? previous?.Destination,
                    gateway ?? previous?.Gateway,
                    clock.UtcNow),
                ++_sequence);
        }

        ViewChanged?.Invoke(this, config.Id);
    }

    public void Remove(ConfigId id)
    {
        bool removed;
        lock (_sync)
        {
            removed = _entries.Remove(id);
        }

        if (removed)
            ViewChanged?.Invoke(this, id);
    }
}
