using Shionji.Domain.ValueObjects;

namespace Shionji.Application;

public readonly record struct SessionLogLine(string Line, bool IsError);

/// <summary>
/// 設定ごとのセッションログ (末尾 200 行) を保持する。
/// 詳細ペインの表示有無にかかわらず全設定分を蓄積し、行選択の切替でログが失われないようにする。
/// </summary>
public sealed class SessionLogStore
{
    private const int MaxLines = 200;

    private readonly object _sync = new();
    private readonly Dictionary<ConfigId, List<SessionLogLine>> _lines = [];

    public event EventHandler<SessionLogEventArgs>? LineAppended;

    public SessionLogStore(TunnelSupervisor supervisor)
    {
        supervisor.SessionLog += (_, e) => Append(e);
    }

    public IReadOnlyList<SessionLogLine> GetLines(ConfigId id)
    {
        lock (_sync)
        {
            return _lines.TryGetValue(id, out var lines) ? [.. lines] : [];
        }
    }

    public void Remove(ConfigId id)
    {
        lock (_sync)
        {
            _lines.Remove(id);
        }
    }

    private void Append(SessionLogEventArgs e)
    {
        lock (_sync)
        {
            if (!_lines.TryGetValue(e.ConfigId, out var lines))
            {
                lines = [];
                _lines[e.ConfigId] = lines;
            }

            lines.Add(new SessionLogLine(e.Line, e.IsError));
            while (lines.Count > MaxLines)
                lines.RemoveAt(0);
        }

        LineAppended?.Invoke(this, e);
    }
}
