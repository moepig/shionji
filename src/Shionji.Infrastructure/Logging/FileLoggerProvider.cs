using System.Text;
using Microsoft.Extensions.Logging;
using Shionji.Domain.Diagnostics;

namespace Shionji.Infrastructure.Logging;

/// <summary>
/// %APPDATA%/Shionji/logs/shionji-yyyyMMdd.log への素朴なファイルロガー。
/// 常駐アプリの事後デバッグ用。14 日より古いログは起動時に削除する。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly object _sync = new();

    public FileLoggerProvider(string directory, int retentionDays = 30)
    {
        _directory = directory;
        _retentionDays = retentionDays;
        Directory.CreateDirectory(directory);
        CleanupOldFiles();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_sync)
        {
            var path = Path.Combine(_directory, $"shionji-{DateTime.Now:yyyyMMdd}.log");
            try
            {
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // ログ書き込み失敗でアプリを止めない
            }
        }
    }

    private void CleanupOldFiles()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "shionji-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            // 監査目的で時刻の解釈がぶれないよう、オフセット付きの ISO 8601 で記録する
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"))
                .Append(" [").Append(Level(logLevel)).Append("] ")
                .Append(category).Append(": ")
                .Append(formatter(state, exception));

            // 画面には出さない詳細フィールドをテキストログにだけ展開する
            if (state is IDetailedLogState detailed && detailed.Details.Count > 0)
            {
                builder.Append(" |");
                foreach (var (key, value) in detailed.Details)
                    builder.Append(' ').Append(key).Append('=').Append(Quote(value));
            }

            if (exception is not null)
                builder.AppendLine().Append(exception);

            provider.Write(builder.ToString());
        }

        /// <summary>空白を含む値は引用符で囲み、key=値 の切れ目が曖昧にならないようにする。</summary>
        private static string Quote(object? value)
        {
            var text = value?.ToString() ?? string.Empty;
            return text.Any(char.IsWhiteSpace) || text.Contains('"')
                ? $"\"{text.Replace("\"", "\"\"")}\""
                : text;
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => level.ToString().ToUpperInvariant(),
        };
    }
}
