using Microsoft.Extensions.Logging;
using Shionji.Domain.Diagnostics;

namespace Shionji.Application;

/// <summary>要約と詳細フィールドを併せ持つログの状態。</summary>
public sealed class DetailedLogState(string summary, IReadOnlyList<KeyValuePair<string, object?>> details)
    : IDetailedLogState
{
    public string Summary { get; } = summary;

    public IReadOnlyList<KeyValuePair<string, object?>> Details { get; } = details;

    /// <summary>詳細を持たないシンク (画面など) では要約だけが見える。</summary>
    public override string ToString() => Summary;
}

public static class AuditLoggerExtensions
{
    /// <summary>
    /// 要約と監査用の詳細を 1 回のログとして記録する。
    /// 値が null や空文字の詳細は落とす。
    /// </summary>
    public static void Audit(
        this ILogger logger,
        LogLevel level,
        string summary,
        params (string Key, object? Value)[] details)
    {
        if (!logger.IsEnabled(level))
            return;

        var fields = details
            .Where(d => d.Value is not null && d.Value.ToString()?.Length > 0)
            .Select(d => new KeyValuePair<string, object?>(d.Key, d.Value))
            .ToArray();

        logger.Log(level, default, new DetailedLogState(summary, fields), null, static (s, _) => s.Summary);
    }
}
