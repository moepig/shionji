using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shionji.Application;

/// <summary>アプリ起動時の一連の処理: 設定ロード → 全件解決 + 起動時自動接続。</summary>
public sealed class StartupService(
    ConfigService configService,
    ResolutionService resolutionService,
    TunnelSupervisor supervisor,
    ILogger<StartupService>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<StartupService>.Instance;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // 監査時に「誰のどの端末での操作か」を辿れるよう、実行環境を最初に残す
        _log.Audit(LogLevel.Information, "Shionji を起動しました",
            ("version", typeof(StartupService).Assembly.GetName().Version?.ToString()),
            ("user", $"{Environment.UserDomainName}\\{Environment.UserName}"),
            ("machine", Environment.MachineName),
            ("process", Environment.ProcessId),
            ("OS", Environment.OSVersion.VersionString));

        await configService.LoadAsync(cancellationToken);

        var configs = configService.Configs;

        // 自動接続する設定は接続フローが最新の解決結果を Publish するため、
        // 事前の全件解決からは除外して AWS API の二重呼び出しを避ける。
        // 解決と接続は互いに待たず並列に走らせる
        var autoConnect = configs.Where(c => c.Options.ConnectOnLaunch).ToList();
        var refreshOnly = configs.Where(c => !c.Options.ConnectOnLaunch).ToList();

        if (autoConnect.Count > 0)
            _log.LogInformation("起動時に {Count} 件の設定へ自動接続します", autoConnect.Count);

        await Task.WhenAll(
            resolutionService.RefreshAllAsync(refreshOnly, cancellationToken),
            Task.WhenAll(autoConnect.Select(c => supervisor.StartAsync(c, cancellationToken))));

        _log.LogInformation("起動処理を完了しました");
    }
}
