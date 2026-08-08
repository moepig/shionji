using System.Collections.Concurrent;
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
/// </summary>
public sealed class ResolutionService(IResourceCatalog catalog, IClock clock)
{
    private readonly ConcurrentDictionary<ConfigId, ConfigResolutionView> _views = new();

    /// <summary>ビューが更新された設定の ID を通知する。</summary>
    public event EventHandler<ConfigId>? ViewChanged;

    public ConfigResolutionView? GetView(ConfigId id) =>
        _views.TryGetValue(id, out var view) ? view : null;

    public async Task RefreshAsync(ForwardingConfig config, CancellationToken cancellationToken = default)
    {
        var previous = GetView(config.Id);
        SetView(new ConfigResolutionView(
            config.Id, IsResolving: true, previous?.Destination, previous?.Gateway, previous?.RefreshedAt));

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

        SetView(new ConfigResolutionView(config.Id, IsResolving: false, destination, gateway, clock.UtcNow));
    }

    /// <summary>全設定を並列に解決する。</summary>
    public Task RefreshAllAsync(IEnumerable<ForwardingConfig> configs, CancellationToken cancellationToken = default) =>
        Task.WhenAll(configs.Select(c => RefreshAsync(c, cancellationToken)));

    /// <summary>接続開始時などに外部で得られた最新の解決結果を反映する。</summary>
    public void Publish(ForwardingConfig config, ResolutionOutcome? destination, ResolutionOutcome? gateway) =>
        SetView(new ConfigResolutionView(config.Id, IsResolving: false, destination, gateway, clock.UtcNow));

    public void Remove(ConfigId id)
    {
        if (_views.TryRemove(id, out _))
            ViewChanged?.Invoke(this, id);
    }

    private void SetView(ConfigResolutionView view)
    {
        _views[view.ConfigId] = view;
        ViewChanged?.Invoke(this, view.ConfigId);
    }
}
