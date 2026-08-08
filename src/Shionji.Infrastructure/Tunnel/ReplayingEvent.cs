namespace Shionji.Infrastructure.Tunnel;

/// <summary>
/// 購読される前に起きた出来事を溜めておき、最初の購読時にまとめて配り直すイベント。
/// トンネルはハンドルが呼び出し元へ渡る前から動いている (ポートが開くまで
/// LaunchAsync が返らない) ため、素のイベントでは起動直後の出力や即時終了を取りこぼす。
/// </summary>
internal sealed class ReplayingEvent<TArgs>(int maxPending = 200)
    where TArgs : EventArgs
{
    private readonly Lock _gate = new();
    private readonly List<TArgs> _pending = [];
    private EventHandler<TArgs>? _handlers;

    /// <summary>一度でも購読されたか。以後は溜めずにそのまま配る。</summary>
    private bool _subscribed;

    public void Add(object sender, EventHandler<TArgs>? handler)
    {
        if (handler is null)
            return;

        TArgs[] replay;
        lock (_gate)
        {
            _handlers += handler;
            _subscribed = true;
            replay = [.. _pending];
            _pending.Clear();
        }

        // 溜まっていた分は、この購読者にだけ配る (既存の購読者は受け取り済み)
        foreach (var args in replay)
            handler(sender, args);
    }

    public void Remove(EventHandler<TArgs>? handler)
    {
        if (handler is null)
            return;

        lock (_gate)
            _handlers -= handler;
    }

    public void Raise(object sender, TArgs args)
    {
        EventHandler<TArgs>? handlers;
        lock (_gate)
        {
            if (!_subscribed)
            {
                _pending.Add(args);
                if (_pending.Count > maxPending)
                    _pending.RemoveAt(0);
                return;
            }

            handlers = _handlers;
        }

        handlers?.Invoke(sender, args);
    }
}
