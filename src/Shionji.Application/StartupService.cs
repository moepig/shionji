namespace Shionji.Application;

/// <summary>アプリ起動時の一連の処理: 設定ロード → 全件解決 + 起動時自動接続。</summary>
public sealed class StartupService(
    ConfigService configService,
    ResolutionService resolutionService,
    TunnelSupervisor supervisor)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await configService.LoadAsync(cancellationToken);

        var configs = configService.Configs;

        // 自動接続する設定は接続フローが最新の解決結果を Publish するため、
        // 事前の全件解決からは除外して AWS API の二重呼び出しを避ける。
        // 解決と接続は互いに待たず並列に走らせる
        var autoConnect = configs.Where(c => c.Options.ConnectOnLaunch).ToList();
        var refreshOnly = configs.Where(c => !c.Options.ConnectOnLaunch).ToList();

        await Task.WhenAll(
            resolutionService.RefreshAllAsync(refreshOnly, cancellationToken),
            Task.WhenAll(autoConnect.Select(c => supervisor.StartAsync(c, cancellationToken))));
    }
}
