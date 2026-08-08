using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Shionji.Presentation;
using Windows.ApplicationModel.DataTransfer;

namespace Shionji.App.WinUI;

/// <summary>DispatcherQueue への投げ込み。UI スレッドで生成すること。</summary>
public sealed class WinUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();

    public void Post(Action action)
    {
        if (!_queue.TryEnqueue(() => action()))
            action();
    }
}

/// <summary>AppNotification によるトースト通知。登録に失敗した場合は黙って無効化する。</summary>
public sealed class WinUiNotificationService : INotificationService
{
    private readonly bool _registered;

    public WinUiNotificationService()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception)
        {
            _registered = false;
        }
    }

    public void NotifyUnexpectedDisconnect(string configName, string message)
    {
        if (!_registered)
            return;

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText($"{configName} が切断されました")
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception)
        {
            // 通知は補助機能。失敗してもアプリを止めない
        }
    }
}

public sealed class WinUiClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}

/// <summary>保存先をエクスプローラーで開く。</summary>
public sealed class WinUiFileLocationService(string logDirectory) : IFileLocationService
{
    public string LogDirectory { get; } = logDirectory;

    public void OpenLogLocation()
    {
        // 当日のログがあればそれを選択した状態で開く
        var today = Path.Combine(LogDirectory, $"shionji-{DateTime.Now:yyyyMMdd}.log");
        if (File.Exists(today))
            Reveal($"/select,\"{today}\"", LogDirectory);
        else
            OpenFolder(LogDirectory);
    }

    public void OpenFolder(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Reveal($"\"{directory}\"", directory);
    }

    private static void Reveal(string arguments, string directoryToCreate)
    {
        try
        {
            Directory.CreateDirectory(directoryToCreate);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", arguments)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // 開けなくてもアプリの動作には影響しない
        }
    }
}
