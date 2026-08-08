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

/// <param name="Operation">JSON プロトコルは X-Amz-Target、Query プロトコルは Action の値。</param>
public sealed record StubRequest(string Operation, string Body)
{
    /// <summary>Query プロトコルのフォームパラメータ。</summary>
    public IReadOnlyDictionary<string, string> Form =>
        Body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')),
                StringComparer.Ordinal);
}

public sealed record StubResponse(string Body, int Status = 200, string ContentType = "application/x-amz-json-1.1")
{
    public static StubResponse Json(object value) =>
        new(JsonSerializer.Serialize(value));

    public static StubResponse Xml(string xml) =>
        new(xml, ContentType: "text/xml");

    public static StubResponse JsonError(string code, string message, int status = 400) =>
        new(JsonSerializer.Serialize(new { __type = code, message }), status);

    public static StubResponse XmlError(string code, string message, int status = 400) =>
        new($"""
            <ErrorResponse><Error><Code>{code}</Code><Message>{message}</Message></Error></ErrorResponse>
            """, status, "text/xml");
}

/// <summary>
/// AWS の JSON / Query 両プロトコルに応答する最小スタブ。
/// 実 SDK のシリアライズ・署名・アンマーシャル経路を通したまま AWS なしで検証するために使う。
/// </summary>
public sealed class StubAwsServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<StubRequest> _requests = new();
    private readonly ConcurrentDictionary<string, Func<StubRequest, StubResponse>> _handlers = new();

    public string Url { get; }

    public IReadOnlyList<StubRequest> Requests => [.. _requests];

    public bool Received(string operation) => _requests.Any(r => r.Operation == operation);

    public int CountOf(string operation) => _requests.Count(r => r.Operation == operation);

    public StubRequest? LastRequest(string operation) =>
        _requests.LastOrDefault(r => r.Operation == operation);

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
            var isJsonProtocol = context.Request.Headers.ContainsKey("X-Amz-Target");
            var request = new StubRequest(OperationOf(context, body), body);
            _requests.Enqueue(request);

            var response = _handlers.TryGetValue(request.Operation, out var handler)
                ? handler(request)
                : isJsonProtocol
                    ? StubResponse.JsonError("UnknownOperationException", request.Operation)
                    : StubResponse.XmlError("InvalidAction", request.Operation);

            context.Response.StatusCode = response.Status;
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body);
        });
    }

    private static string OperationOf(HttpContext context, string body)
    {
        // JSON プロトコル: "AmazonSSM.StartSession" のようにサービス名込みで入る
        if (context.Request.Headers.TryGetValue("X-Amz-Target", out var target))
        {
            var raw = target.ToString();
            return raw.Contains('.') ? raw[(raw.LastIndexOf('.') + 1)..] : raw;
        }

        // Query プロトコル: フォームの Action
        return new StubRequest(string.Empty, body).Form.GetValueOrDefault("Action", "Unknown");
    }

    public static async Task<StubAwsServer> StartAsync()
    {
        var server = new StubAwsServer(FreePort());
        await server._app.StartAsync();
        return server;
    }

    public StubAwsServer On(string operation, Func<StubRequest, StubResponse> handler)
    {
        _handlers[operation] = handler;
        return this;
    }

    public StubAwsServer On(string operation, StubResponse response) =>
        On(operation, _ => response);

    /// <summary>ポートフォワード用の StartSession / TerminateSession を既定応答で登録する。</summary>
    public StubAwsServer WithSsmSession(string sessionId = "shionji-test-session") =>
        On("StartSession", StubResponse.Json(new
        {
            SessionId = sessionId,
            TokenValue = "fake-token-value",
            StreamUrl = "wss://127.0.0.1/fake-stream",
        }))
        .On("TerminateSession", StubResponse.Json(new { SessionId = sessionId }));

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
