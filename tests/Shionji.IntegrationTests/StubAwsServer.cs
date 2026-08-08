using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shionji.IntegrationTests;

public sealed record StubRequest(string Target, string Body);

/// <summary>
/// AWS JSON プロトコルの最小スタブ。X-Amz-Target ヘッダで操作を振り分ける。
/// 実 SDK のシリアライズ・署名・HTTP 経路を通したまま、AWS なしで検証するために使う。
/// </summary>
public sealed class StubAwsServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<StubRequest> _requests = new();
    private readonly ConcurrentDictionary<string, Func<string, string>> _handlers = new();

    public string Url { get; }

    public IReadOnlyList<StubRequest> Requests => [.. _requests];

    public bool Received(string target) => _requests.Any(r => r.Target == target);

    public StubRequest? LastRequest(string target) => _requests.LastOrDefault(r => r.Target == target);

    private StubAwsServer(int port)
    {
        Url = $"http://127.0.0.1:{port}";

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(Url);
        _app = builder.Build();

        _app.MapPost("/", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();

            // JSON プロトコルは "AmazonSSM.StartSession" のようにサービス名込みで入る
            var rawTarget = context.Request.Headers["X-Amz-Target"].ToString();
            var target = rawTarget.Contains('.') ? rawTarget[(rawTarget.LastIndexOf('.') + 1)..] : rawTarget;
            _requests.Enqueue(new StubRequest(target, body));

            if (!_handlers.TryGetValue(target, out var handler))
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/x-amz-json-1.1";
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(new { __type = "UnknownOperationException", message = target }));
                return;
            }

            context.Response.ContentType = "application/x-amz-json-1.1";
            await context.Response.WriteAsync(handler(body));
        });
    }

    public static async Task<StubAwsServer> StartAsync()
    {
        var server = new StubAwsServer(FreePort());
        await server._app.StartAsync();
        return server;
    }

    /// <summary>操作名 (例: "StartSession") に対する応答 JSON を登録する。</summary>
    public StubAwsServer On(string target, Func<string, string> handler)
    {
        _handlers[target] = handler;
        return this;
    }

    public StubAwsServer On(string target, object response) =>
        On(target, _ => JsonSerializer.Serialize(response));

    /// <summary>ポートフォワード用の StartSession / TerminateSession を既定応答で登録する。</summary>
    public StubAwsServer WithSsmSession(string sessionId = "shionji-test-session") =>
        On("StartSession", new
        {
            SessionId = sessionId,
            TokenValue = "fake-token-value",
            StreamUrl = "wss://127.0.0.1/fake-stream",
        })
        .On("TerminateSession", new { SessionId = sessionId });

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
