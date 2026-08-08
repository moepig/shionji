using System.IO;
using Microsoft.UI.Xaml;
using Shionji.Presentation;

namespace Shionji.App.WinUI;

/// <summary>アプリ設定ウィンドウ (接続先設定とは別)。</summary>
public sealed partial class SettingsWindow : Window
{
    private static readonly string IconPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "shionji.ico");

    public SettingsWindow(AppSettingsViewModel settings, ThemeHost themeHost)
    {
        InitializeComponent();

        Title = "設定";
        RootGrid.DataContext = settings;

        if (File.Exists(IconPath))
            AppWindow.SetIcon(IconPath);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 820));

        themeHost.Register(this);
        settings.Closed += (_, _) => DispatcherQueue.TryEnqueue(Close);
    }
}

/// <summary>設定ウィンドウを開く。すでに開いていれば開き直す。</summary>
public sealed class WinUiSettingsWindowService(ThemeHost themeHost) : ISettingsWindowService
{
    private SettingsWindow? _current;

    public void ShowSettings(AppSettingsViewModel settings)
    {
        _current?.Close();

        var window = new SettingsWindow(settings, themeHost);
        _current = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, window))
                _current = null;
        };
        window.Activate();
    }
}
