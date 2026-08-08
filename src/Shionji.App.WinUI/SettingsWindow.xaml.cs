using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shionji.Presentation;

namespace Shionji.App.WinUI;

/// <summary>アプリ設定ウィンドウ (接続先設定とは別)。</summary>
public sealed partial class SettingsWindow : Window
{
    private static readonly string IconPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "shionji.ico");

    private readonly AppSettingsViewModel _settings;

    public SettingsWindow(AppSettingsViewModel settings, ThemeHost themeHost)
    {
        InitializeComponent();

        _settings = settings;
        Title = "設定";
        RootGrid.DataContext = settings;

        if (File.Exists(IconPath))
            AppWindow.SetIcon(IconPath);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 620));

        themeHost.Register(this);
        settings.Closed += (_, _) => DispatcherQueue.TryEnqueue(Close);

        // × で閉じてもキャンセル扱いにする (試用中のテーマを残さない)
        Closed += (_, _) => settings.HandleWindowClosed();
    }

    /// <summary>左のメニューの選択を ViewModel へ伝える。表示の切り替え自体は束縛で行う。</summary>
    private void OnSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }
            && Enum.TryParse<SettingsSection>(tag, out var section))
        {
            _settings.Section = section;
        }
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
