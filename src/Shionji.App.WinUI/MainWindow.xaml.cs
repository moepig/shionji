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
    private readonly MainViewModel _viewModel;
    private readonly TunnelSupervisor _supervisor;
    private readonly AppSettingsStore _settings;
    private readonly ThemeHost _themeHost;
    private TaskbarIcon? _trayIcon;
    private bool _exiting;

    /// <summary>終了の確認を出している最中か。× とトレイの両方から重ねて呼ばれうる。</summary>
    private bool _confirming;

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

        this.ApplyAppIcon();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 720));
        AppWindow.Closing += OnClosing;
        AppWindow.Changed += OnAppWindowChanged;

        // 保存済みのカラーテーマを反映し、以降このウィンドウにも追従させる
        _themeHost = services.GetRequiredService<ThemeHost>();
        _themeHost.Register(this);

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

    /// <summary>メニューとタスクトレイの「終了」。トレイ格納ではなく、全セッションを畳んで本当に終了する。</summary>
    private void OnExitRequested(object sender, RoutedEventArgs args) => _ = ExitAsync();

    /// <summary>
    /// タイトルバーの × は終了として扱う。トレイへ格納するのは最小化のときだけである。
    /// 全セッションを畳んでからでないと終われないため、いったん取り消して終了処理へ渡す。
    /// </summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting)
            return;

        args.Cancel = true;
        _ = ExitAsync();
    }

    /// <summary>
    /// 最小化されたらタスクトレイへ格納する。最小化は presenter の状態変化として届くため、
    /// ウィンドウの変更通知から拾う。
    /// </summary>
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange || !_settings.Current.HideOnMinimize || _trayIcon is null)
            return;

        if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            sender.Hide();
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
        if (AppIcon.Exists)
            _trayIcon.IconSource = new BitmapImage(new Uri(AppIcon.FilePath));

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

    /// <summary>
    /// 設定に従い、ウィンドウを出さずタスクトレイへ格納した状態で始める。
    /// 格納先が無い (トレイを作れなかった) 場合は格納しない。
    /// </summary>
    /// <returns>格納した場合は true、表示して始める場合は false。</returns>
    internal bool StartInTray()
    {
        if (!_settings.Current.StartMinimized || _trayIcon is null)
            return false;

        AppWindow.Hide();
        return true;
    }

    internal void ShowFromTray()
    {
        AppWindow.Show();

        // 最小化から格納した場合、出すだけでは最小化のままになる
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();

        Activate();
    }

    /// <summary>
    /// 終了してよいか尋ねる。確認を出さない設定なら、そのまま終了へ進む。
    /// タスクトレイへ格納している間はダイアログを出す先が無いため、先にウィンドウを戻す。
    /// </summary>
    /// <returns>終了してよい場合は true。取り消した場合は false。</returns>
    private async Task<bool> ConfirmExitAsync()
    {
        if (ExitPrompt.For(_settings.Current.ConfirmOnExit, _viewModel.ConnectedCount) is not { } prompt)
            return true;

        // 既に尋ねている最中なら、二重にダイアログを出さずそちらの返事に任せる
        if (_confirming)
        {
            ShowFromTray();
            return false;
        }

        ShowFromTray();

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            // ダイアログは視覚ツリーの外に出るため、テーマを引き継がない。ここで揃える
            RequestedTheme = _themeHost.Current,
            Title = prompt.Title,
            Content = prompt.Message,
            PrimaryButtonText = "終了",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };

        _confirming = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _confirming = false;
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting)
            return;

        if (!await ConfirmExitAsync())
            return;

        _exiting = true;

        // 先にトレイから消す。畳んでいる間にもう一度「終了」を押せてしまうのを防ぐ
        _trayIcon?.Dispose();
        _trayIcon = null;

        try
        {
            await _supervisor.StopAllAsync();
        }
        catch (Exception)
        {
            // 終了を妨げない
        }

        // トレイへ格納してウィンドウを隠している間は Application.Exit ではプロセスが残るため、
        // 後始末を終えたこの時点でプロセス自体を終える
        Environment.Exit(0);
    }
}
