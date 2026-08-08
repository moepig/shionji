namespace Shionji.Application;

/// <summary>アプリ起動時の一連の処理: 設定ロード → 全件並列解決 → 起動時自動接続。</summary>
public sealed class StartupService(
    ConfigService configService,
    ResolutionService resolutionService,
    TunnelSupervisor supervisor)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await configService.LoadAsync(cancellationToken);

        var configs = configService.Configs;
        await resolutionService.RefreshAllAsync(configs, cancellationToken);

        var launches = configs
            .Where(c => c.Options.ConnectOnLaunch)
            .Select(c => supervisor.StartAsync(c, cancellationToken));
        await Task.WhenAll(launches);
    }
}
