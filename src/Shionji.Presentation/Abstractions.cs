namespace Shionji.Presentation;

/// <summary>UI スレッドへの処理の投げ込み。WinUI 側で DispatcherQueue により実装する。</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>トースト通知。WinUI 側で AppNotification により実装する。</summary>
public interface INotificationService
{
    void NotifyUnexpectedDisconnect(string configName, string message);
}

/// <summary>クリップボード。</summary>
public interface IClipboardService
{
    void SetText(string text);
}
