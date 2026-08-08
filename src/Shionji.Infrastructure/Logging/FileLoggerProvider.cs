using System.Text;
using Microsoft.Extensions.Logging;

namespace Shionji.Infrastructure.Logging;

/// <summary>
/// %APPDATA%/Shionji/logs/shionji-yyyyMMdd.log への素朴なファイルロガー。
/// 常駐アプリの事後デバッグ用。14 日より古いログは起動時に削除する。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _sync = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
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
            var cutoff = DateTime.Now.AddDays(-14);
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

            var builder = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(Level(logLevel)).Append("] ")
                .Append(category).Append(": ")
                .Append(formatter(state, exception));
            if (exception is not null)
                builder.AppendLine().Append(exception);

            provider.Write(builder.ToString());
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
