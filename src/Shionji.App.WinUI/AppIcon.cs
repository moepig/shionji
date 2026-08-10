using Microsoft.UI.Xaml;

namespace Shionji.App.WinUI;

/// <summary>アプリのアイコン。どのウィンドウとタスクトレイでも同じものを使う。</summary>
internal static class AppIcon
{
    /// <summary>アイコンファイルの場所。</summary>
    internal static string FilePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "Assets", "shionji.ico");

    /// <summary>アイコンファイルがあるか。</summary>
    internal static bool Exists => File.Exists(FilePath);

    /// <summary>
    /// ウィンドウのアイコンを設定する。
    /// ファイルが無い場合は既定のアイコンのままにする (見た目だけの問題であり、動作は妨げない)。
    /// </summary>
    internal static void ApplyAppIcon(this Window window)
    {
        if (Exists)
            window.AppWindow.SetIcon(FilePath);
    }
}
