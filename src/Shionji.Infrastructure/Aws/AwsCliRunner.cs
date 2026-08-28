using System.Diagnostics;
using System.Text;

namespace Shionji.Infrastructure.Aws;

internal interface IAwsCliRunner
{
    Task<int> RunAsync(IReadOnlyList<string> arguments, Action<string> output, CancellationToken cancellationToken);
}

/// <summary>標準出力と標準エラーを同時に読み、キャンセル時は子プロセスも終了する。</summary>
internal sealed class AwsCliRunner(string browserCommand, string executable = "aws") : IAwsCliRunner
{
    internal ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        info.Environment["AWS_CLI_AUTO_PROMPT"] = "off";
        info.Environment["AWS_PAGER"] = "";
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUNBUFFERED"] = "1";
        // aws login に --no-browser はない。CLI のブラウザ呼び出しだけを
        // 即時終了するヘルパーモードへ向け、出力を読んだ親アプリが一度だけ開く。
        info.Environment["BROWSER"] = browserCommand;
        return info;
    }

    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments, Action<string> output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = CreateStartInfo(arguments) };
        process.Start();
        // プロファイルの別アカウントへの上書きなどを勝手に承諾しない。
        // 追加入力が必要な場合は EOF として終了させ、CLI での操作を案内する。
        process.StandardInput.Close();
        using var registration = cancellationToken.Register(() => Kill(process));
        var outputLock = new object();
        async Task ReadAsync(StreamReader reader)
        {
            try
            {
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    lock (outputLock)
                        output(line);
                }
            }
            catch
            {
                Kill(process);
                throw;
            }
        }

        var stdout = ReadAsync(process.StandardOutput);
        var stderr = ReadAsync(process.StandardError);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
