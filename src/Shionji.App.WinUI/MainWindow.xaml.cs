using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Shionji.Application;
using Shionji.Infrastructure.Storage;
using Shionji.Presentation;

namespace Shionji.App.WinUI;

public sealed partial class MainWindow : Window
{
    private static readonly string IconPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "shionji.ico");

    private readonly MainViewModel _viewModel;
    private readonly TunnelSupervisor _supervisor;
    private readonly AppSettingsStore _settings;
    private TaskbarIcon? _trayIcon;
    private bool _exiting;

    public MainWindow(IServiceProvider services, bool isDemoMode)
    {
        InitializeComponent();

        _viewModel = services.GetRequiredService<MainViewModel>();
        _supervisor = services.GetRequiredService<TunnelSupervisor>();
        _settings = services.GetRequiredService<AppSettingsStore>();

        Title = isDemoMode ? "Shionji (デモモード)" : "Shionji";
        DemoBadge.Visibility = isDemoMode ? Visibility.Visible : Visibility.Collapsed;
        RootGrid.DataContext = _viewModel;
        // Flyout の中身は視覚ツリーの外に置かれるため DataContext を明示する
        ActivityPanel.DataContext = _viewModel;

        if (File.Exists(IconPath))
            AppWindow.SetIcon(IconPath);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 720));
        AppWindow.Closing += OnClosing;

        SetupTrayIcon();

        var startup = services.GetRequiredService<StartupService>();
        _ = RunStartupAsync(startup);
    }

    private static async Task RunStartupAsync(StartupService startup)
    {
        try
        {
            await startup.RunAsync();
        }
        catch (Exception)
        {
            // 起動時の失敗は各行の状態表示に現れる
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) =>
        _viewModel.FilterText = FilterBox.Text;

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_exiting && _settings.Current.MinimizeToTray && _trayIcon is not null)
        {
            // トレイへ格納
            args.Cancel = true;
            AppWindow.Hide();
            return;
        }

        if (!_exiting)
        {
            // 全セッションを畳んでから終了する
            args.Cancel = true;
            _ = ExitAsync();
        }
    }

    private void SetupTrayIcon()
    {
        var flyout = new MenuFlyout();

        var open = new MenuFlyoutItem { Text = "開く" };
        open.Click += (_, _) => ShowFromTray();
        flyout.Items.Add(open);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var exit = new MenuFlyoutItem { Text = "終了" };
        exit.Click += (_, _) => _ = ExitAsync();
        flyout.Items.Add(exit);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Shionji",
            ContextFlyout = flyout,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(ShowFromTray),
        };
        if (File.Exists(IconPath))
            _trayIcon.IconSource = new BitmapImage(new Uri(IconPath));

        try
        {
            _trayIcon.ForceCreate();
        }
        catch (Exception)
        {
            // トレイが作れない環境ではウィンドウのみで動作させる
            _trayIcon = null;
        }
    }

    internal void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    private async Task ExitAsync()
    {
        if (_exiting)
            return;
        _exiting = true;

        try
        {
            await _supervisor.StopAllAsync();
        }
        catch (Exception)
        {
            // 終了を妨げない
        }

        _trayIcon?.Dispose();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
