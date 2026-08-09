using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shionji.Domain.Diagnostics;

namespace Shionji.Infrastructure.Logging;

/// <summary>
/// %APPDATA%/Shionji/logs/shionji-yyyyMMdd.log への素朴なファイルロガー。
/// 1 行 1 JSON オブジェクト (JSON Lines) で書く。常駐アプリの事後デバッグ用。
/// 14 日より古いログは起動時に削除する。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>BOM を書かない UTF-8。先頭行に BOM が挟まると JSON として読めなくなるため。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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
                File.AppendAllText(path, line + Environment.NewLine, Utf8NoBom);
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
        /// <summary>
        /// 日本語を \uXXXX へ落とさず、そのまま読める形で書く。
        /// 出力先はファイルであり、HTML や URL へ埋め込まれることはない。
        /// </summary>
        private static readonly JsonWriterOptions WriterOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

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

            var buffer = new ArrayBufferWriter<byte>(256);
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                writer.WriteStartObject();

                // 監査目的で時刻の解釈がぶれないよう、オフセット付きの ISO 8601 で記録する
                writer.WriteString("timestamp", DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz"));
                writer.WriteString("level", Level(logLevel));
                writer.WriteString("category", category);
                writer.WriteString("message", formatter(state, exception));

                // 画面には出さない詳細フィールドをファイルにだけ展開する。
                // 入れ子にするのは、詳細のキーが timestamp などの外枠と衝突しないようにするため
                if (state is IDetailedLogState detailed && detailed.Details.Count > 0)
                {
                    writer.WriteStartObject("details");
                    foreach (var (key, value) in detailed.Details)
                    {
                        writer.WritePropertyName(key);
                        WriteValue(writer, value);
                    }

                    writer.WriteEndObject();
                }

                if (exception is not null)
                    writer.WriteString("exception", exception.ToString());

                writer.WriteEndObject();
            }

            provider.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
        }

        /// <summary>数値と真偽値は JSON の型を保ち、それ以外は文字列にする。</summary>
        private static void WriteValue(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    break;
                case int number:
                    writer.WriteNumberValue(number);
                    break;
                case long number:
                    writer.WriteNumberValue(number);
                    break;
                case double number:
                    writer.WriteNumberValue(number);
                    break;
                case decimal number:
                    writer.WriteNumberValue(number);
                    break;
                default:
                    writer.WriteStringValue(value.ToString());
                    break;
            }
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
