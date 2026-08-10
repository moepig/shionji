using Microsoft.UI.Xaml;
using Shionji.Presentation;

namespace Shionji.App.WinUI;

/// <summary>設定の追加 / 編集を行う独立ウィンドウ。</summary>
public sealed partial class ConfigEditorWindow : Window
{
    public ConfigEditorWindow(ConfigEditorViewModel editor, ThemeHost themeHost)
    {
        InitializeComponent();

        Title = editor.WindowTitle;
        EditorView.DataContext = editor;

        this.ApplyAppIcon();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 900));
        themeHost.Register(this);

        // 保存 / キャンセルでウィンドウを閉じる
        editor.Closed += (_, _) => DispatcherQueue.TryEnqueue(Close);
    }
}

/// <summary>編集ウィンドウを開く。すでに開いていれば前面に出すだけにする。</summary>
public sealed class WinUiConfigEditorWindowService(ThemeHost themeHost) : IConfigEditorWindowService
{
    private ConfigEditorWindow? _current;

    public void ShowEditor(ConfigEditorViewModel editor)
    {
        _current?.Close();

        var window = new ConfigEditorWindow(editor, themeHost);
        _current = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, window))
                _current = null;
        };
        window.Activate();
    }
}
