using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Shionji.Application;
using Shionji.Domain.Ports;
using Shionji.Infrastructure;
using Shionji.Infrastructure.Aws;
using Shionji.Infrastructure.Fakes;
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
        _services = BuildServices();
        _window = new MainWindow(_services, IsDemoMode);
        _window.Activate();
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IRetryScheduler, TaskDelayRetryScheduler>();
        services.AddSingleton<ILocalPortProbe, TcpLocalPortProbe>();

        var settingsStore = new AppSettingsStore(AppSettingsStore.DefaultPath);
        settingsStore.Load();
        services.AddSingleton(settingsStore);

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
            services.AddSingleton<AwsClientFactory>();
            services.AddSingleton<IResourceCatalog, AwsResourceCatalog>();
            services.AddSingleton(new SessionManagerPluginLocator(() => settingsStore.Current.PluginPath));
            services.AddSingleton<ITunnelLauncher, SessionManagerPluginLauncher>();
            services.AddSingleton<ISsoLoginService, SsoLoginService>();
            services.AddSingleton<IForwardingConfigRepository>(
                _ => new JsonForwardingConfigRepository(JsonForwardingConfigRepository.DefaultPath));
        }

        services.AddSingleton<ResolutionService>();
        services.AddSingleton<TunnelSupervisor>();
        services.AddSingleton<ConfigService>();
        services.AddSingleton<StartupService>();

        services.AddSingleton<IUiDispatcher, WinUiDispatcher>();
        services.AddSingleton<INotificationService, WinUiNotificationService>();
        services.AddSingleton<IClipboardService, WinUiClipboardService>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
