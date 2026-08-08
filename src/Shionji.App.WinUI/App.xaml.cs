using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Shionji.Application;
using Shionji.Domain.Ports;
using Shionji.Infrastructure;
using Shionji.Infrastructure.Aws;
using Shionji.Infrastructure.Fakes;
using Shionji.Infrastructure.Logging;
using Shionji.Infrastructure.Storage;
using Shionji.Infrastructure.Tunnel;
using Shionji.Presentation;

namespace Shionji.App.WinUI;

/// <summary>DI 構成と起動のみを担う。ロジックはすべて下位層にある。</summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private ServiceProvider? _services;
    private MainWindow? _window;

    public bool IsDemoMode { get; } =
        Environment.GetCommandLineArgs().Contains("--demo", StringComparer.OrdinalIgnoreCase);

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!EnsureSingleInstance())
            return;

        _services = BuildServices();
        _window = new MainWindow(_services, IsDemoMode);
        _window.Activate();
    }

    /// <summary>
    /// 多重起動を防ぐ。既に起動済みなら起動要求をそちらへ回して終了する
    /// (トレイ常駐中に再起動しても既存ウィンドウが前面に出るだけになる)。
    /// デモモードと通常モードは別インスタンスとして扱う。
    /// </summary>
    private bool EnsureSingleInstance()
    {
        var key = IsDemoMode ? "shionji-demo" : "shionji-main";
        var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey(key);

        if (!mainInstance.IsCurrent)
        {
            var activationArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            // UI スレッドをブロックしないようスレッドプールで完了を待つ
            Task.Run(() => mainInstance.RedirectActivationToAsync(activationArgs).AsTask()).Wait();
            Environment.Exit(0);
            return false;
        }

        mainInstance.Activated += (_, _) =>
            _window?.DispatcherQueue.TryEnqueue(() => _window.ShowFromTray());
        return true;
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IClock, SystemClock>();

        // アプリ設定を先に読む (保存先の指定もここに入っている)
        var settingsStore = new AppSettingsStore();
        var settings = settingsStore.Load();
        services.AddSingleton(settingsStore);

        // ログはファイルと画面のステータスバーの両方へ流す
        var logDirectory = AppPaths.ResolveLogDirectory(settings);
        var activityLog = new ActivityLog(new SystemClock());
        services.AddSingleton(activityLog);
        services.AddSingleton<IFileLocationService>(new WinUiFileLocationService(logDirectory));
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(logDirectory, settingsStore.Current.LogRetentionDays));
            builder.AddProvider(new ActivityLogProvider(activityLog));
        });

        services.AddSingleton<IRetryScheduler, TaskDelayRetryScheduler>();
        services.AddSingleton<ILocalPortProbe, TcpLocalPortProbe>();

        if (IsDemoMode)
        {
            services.AddSingleton<FakeSsoState>();
            services.AddSingleton<IResourceCatalog, FakeResourceCatalog>();
            services.AddSingleton<ITunnelLauncher, FakeTunnelLauncher>();
            services.AddSingleton<ISsoLoginService, FakeSsoLoginService>();
            services.AddSingleton<IForwardingConfigRepository>(
                _ => new InMemoryConfigRepository([.. DemoData.Configs()]));
        }
        else
        {
            services.AddSingleton(new AwsClientFactory(settingsStore.Current.AwsEndpointOverride));
            services.AddSingleton<IResourceCatalog, AwsResourceCatalog>();
            services.AddSingleton(new SessionManagerPluginLocator(() => settingsStore.Current.PluginPath));
            services.AddSingleton<ITunnelLauncher, SessionManagerPluginLauncher>();
            services.AddSingleton<ISsoLoginService>(_ => new SsoLoginService());
            services.AddSingleton<IForwardingConfigRepository>(sp =>
                new JsonForwardingConfigRepository(
                    AppPaths.ResolveConfigsFilePath(settings),
                    sp.GetService<ILogger<JsonForwardingConfigRepository>>()));
        }

        services.AddSingleton<ResolutionService>();
        services.AddSingleton<TunnelSupervisor>();
        services.AddSingleton<SessionLogStore>();
        services.AddSingleton<ConfigService>();
        services.AddSingleton<StartupService>();

        services.AddSingleton<IUiDispatcher, WinUiDispatcher>();
        services.AddSingleton<INotificationService, WinUiNotificationService>();
        services.AddSingleton<IClipboardService, WinUiClipboardService>();

        var themeHost = new ThemeHost();
        themeHost.Apply(WinUiAppSettingsService.ThemeOf(AppThemes.Parse(settings.Theme)));
        services.AddSingleton(themeHost);
        services.AddSingleton<IConfigEditorWindowService, WinUiConfigEditorWindowService>();
        services.AddSingleton<ISettingsWindowService, WinUiSettingsWindowService>();
        services.AddSingleton<IAppSettingsService, WinUiAppSettingsService>();
        services.AddSingleton<IFolderPickerService>(
            new WinUiFolderPickerService(() => WinRT.Interop.WindowNative.GetWindowHandle(_window!)));
        services.AddSingleton<AppSettingsContext>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
