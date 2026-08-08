using System.Net;
using System.Net.Sockets;
using System.Text.Json;

// session-manager-plugin.exe の代役。本物と同じ引数列を受け取り、実際にローカルポートを
// listen する。AWS も SSM も要らないまま、SessionManagerPluginLauncher /
// TunnelProcessHandle のプロセス契約 (引数・確立検知・終了検知・停止) を検証できる。
//
// 引数 (本物と同順):
//   [0] セッション応答 JSON  [1] リージョン  [2] "StartSession"
//   [3] プロファイル名       [4] リクエスト JSON  [5] SSM エンドポイント
//
// 環境変数:
//   SHIONJI_FAKE_PLUGIN_MODE       normal (既定) | exit-before-open | hang | drop-after
//   SHIONJI_FAKE_PLUGIN_DROP_AFTER_MS  drop-after モードでの確立後の生存時間 (既定 2000)
//   SHIONJI_FAKE_PLUGIN_ARGS_FILE  受け取った引数を JSON 配列で書き出すパス
//   SHIONJI_FAKE_PLUGIN_QUIET      1 なら確立行を出力しない (ポートは開く)

var mode = Environment.GetEnvironmentVariable("SHIONJI_FAKE_PLUGIN_MODE") ?? "normal";

if (Environment.GetEnvironmentVariable("SHIONJI_FAKE_PLUGIN_ARGS_FILE") is { Length: > 0 } argsFile)
{
    Directory.CreateDirectory(Path.GetDirectoryName(argsFile)!);
    File.WriteAllText(argsFile, JsonSerializer.Serialize(args));
}

if (args.Length < 6)
{
    Console.Error.WriteLine($"引数が足りません (期待 6, 実際 {args.Length})。");
    return 2;
}

if (mode == "exit-before-open")
{
    Console.Error.WriteLine("Failed to open the local port (fake).");
    return 1;
}

if (mode == "hang")
{
    // 何も出力せずポートも開かない。確立タイムアウト経路の再現
    await Task.Delay(Timeout.Infinite);
    return 0;
}

string sessionId;
int localPort;
try
{
    sessionId = JsonDocument.Parse(args[0]).RootElement.GetProperty("SessionId").GetString() ?? "unknown";
    var parameters = JsonDocument.Parse(args[4]).RootElement.GetProperty("Parameters");
    localPort = int.Parse(parameters.GetProperty("localPortNumber")[0].GetString()!);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"引数の解析に失敗しました: {ex.Message}");
    return 2;
}

TcpListener listener;
try
{
    listener = new TcpListener(IPAddress.Loopback, localPort);
    listener.Start();
}
catch (SocketException ex)
{
    Console.Error.WriteLine($"ポート {localPort} を開けません: {ex.Message}");
    return 1;
}

if (Environment.GetEnvironmentVariable("SHIONJI_FAKE_PLUGIN_QUIET") != "1")
    Console.WriteLine($"Port {localPort} opened for sessionId {sessionId}.");

using var shutdown = new CancellationTokenSource();

if (mode == "drop-after")
{
    var dropAfterMs = int.TryParse(
        Environment.GetEnvironmentVariable("SHIONJI_FAKE_PLUGIN_DROP_AFTER_MS"), out var parsed)
        ? parsed
        : 2000;
    _ = Task.Run(async () =>
    {
        await Task.Delay(dropAfterMs);
        Console.Error.WriteLine("Connection reset by peer (fake).");
        Environment.Exit(1);
    });
}

// 接続をエコーバックする。トンネル越しに実データが流れることをテストで確認できる
try
{
    while (!shutdown.IsCancellationRequested)
    {
        var client = await listener.AcceptTcpClientAsync(shutdown.Token);
        _ = Task.Run(async () =>
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                var buffer = new byte[4096];
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                    await stream.WriteAsync(buffer.AsMemory(0, read));
            }
        });
    }
}
catch (OperationCanceledException)
{
}

return 0;
