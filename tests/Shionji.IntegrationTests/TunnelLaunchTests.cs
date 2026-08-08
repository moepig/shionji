using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;

namespace Shionji.IntegrationTests;

/// <summary>
/// 偽 plugin (実プロセス) + スタブ SSM で SessionManagerPluginLauncher を通しで検証する。
/// AWS も本物の plugin も不要。
/// </summary>
[NotInParallel]
public class TunnelLaunchTests
{
    [Test]
    public async Task 起動するとローカルポートが開きデータが往復する()
    {
        await using var harness = await TunnelHarness.CreateAsync();
        var plan = harness.PlanForRemoteHost();

        var launched = await harness.Launcher.LaunchAsync(plan);

        await Assert.That(launched.IsSuccess).IsTrue();
        await using var handle = launched.Value;

        await Assert.That(handle.LocalPort).IsEqualTo(plan.LocalPort);
        await Assert.That(harness.PortProbe.IsListening(plan.LocalPort)).IsTrue();

        // 実際にトンネル (偽 plugin のエコー) を通してバイトが往復することを確認する
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", plan.LocalPort.Value);
        await using var stream = client.GetStream();
        var payload = Encoding.UTF8.GetBytes("shionji");
        await stream.WriteAsync(payload);
        var buffer = new byte[payload.Length];
        var read = await stream.ReadAtLeastAsync(buffer, payload.Length);
        await Assert.That(Encoding.UTF8.GetString(buffer, 0, read)).IsEqualTo("shionji");
    }

    [Test]
    public async Task pluginへの引数はAWSCLIと同じ順序と内容になる()
    {
        await using var harness = await TunnelHarness.CreateAsync();
        var plan = harness.PlanForRemoteHost("cache.example.internal", 6379);

        var launched = await harness.Launcher.LaunchAsync(plan);
        await Assert.That(launched.IsSuccess).IsTrue();
        await using var handle = launched.Value;

        var args = harness.ReceivedPluginArgs();
        await Assert.That(args.Length).IsEqualTo(6);

        // [0] StartSession の応答がそのまま渡る (スタブが返した値と一致すること)
        using var session = JsonDocument.Parse(args[0]);
        await Assert.That(session.RootElement.GetProperty("SessionId").GetString())
            .IsEqualTo("shionji-test-session");
        await Assert.That(session.RootElement.GetProperty("TokenValue").GetString())
            .IsEqualTo("fake-token-value");

        await Assert.That(args[1]).IsEqualTo("ap-northeast-1");
        await Assert.That(args[2]).IsEqualTo("StartSession");
        await Assert.That(args[3]).IsEqualTo("test");

        using var request = JsonDocument.Parse(args[4]);
        await Assert.That(request.RootElement.GetProperty("DocumentName").GetString())
            .IsEqualTo("AWS-StartPortForwardingSessionToRemoteHost");
        var parameters = request.RootElement.GetProperty("Parameters");
        await Assert.That(parameters.GetProperty("host")[0].GetString()).IsEqualTo("cache.example.internal");
        await Assert.That(parameters.GetProperty("portNumber")[0].GetString()).IsEqualTo("6379");
        await Assert.That(parameters.GetProperty("localPortNumber")[0].GetString())
            .IsEqualTo(plan.LocalPort.Value.ToString());

        await Assert.That(args[5]).IsEqualTo("https://ssm.ap-northeast-1.amazonaws.com");
    }

    [Test]
    public async Task StartSessionのリクエストがSSMへ届く()
    {
        await using var harness = await TunnelHarness.CreateAsync();
        var plan = harness.PlanForDirect(22);

        var launched = await harness.Launcher.LaunchAsync(plan);
        await Assert.That(launched.IsSuccess).IsTrue();
        await using var handle = launched.Value;

        var request = harness.Aws.LastRequest("StartSession");
        await Assert.That(request).IsNotNull();
        using var body = JsonDocument.Parse(request!.Body);
        await Assert.That(body.RootElement.GetProperty("Target").GetString()).IsEqualTo("i-0123456789abcdef0");
        await Assert.That(body.RootElement.GetProperty("DocumentName").GetString())
            .IsEqualTo("AWS-StartPortForwardingSession");
    }

    [Test]
    public async Task 停止するとTerminateSessionを呼びポートが解放される()
    {
        await using var harness = await TunnelHarness.CreateAsync();
        var plan = harness.PlanForRemoteHost();
        var launched = await harness.Launcher.LaunchAsync(plan);
        var handle = launched.Value;

        await handle.StopAsync();

        await Assert.That(harness.Aws.Received("TerminateSession")).IsTrue();
        await WaitUntilAsync(() => !harness.PortProbe.IsListening(plan.LocalPort));
        await handle.DisposeAsync();
    }

    [Test]
    public async Task 確立後に落ちるとExitedイベントが発火する()
    {
        await using var harness = await TunnelHarness.CreateAsync(mode: "drop-after", dropAfterMs: 700);
        var plan = harness.PlanForRemoteHost();
        var launched = await harness.Launcher.LaunchAsync(plan);
        await using var handle = launched.Value;

        ErrorDetail? exitError = null;
        handle.Exited += (_, e) => exitError = e.Error;

        await WaitUntilAsync(() => exitError is not null, timeoutMs: 5000);

        await Assert.That(exitError!.Phase).IsEqualTo(FailurePhase.Plugin);
        await Assert.That(exitError.Code).IsEqualTo("PluginExited");
        // plugin の stderr が原因欄に添えられる
        await Assert.That(exitError.Message).Contains("Connection reset by peer");
    }

    [Test]
    public async Task 確立を待っている間の出力も購読者に届く()
    {
        // plugin はポートが開くまでの間に出力するが、LaunchAsync はポートが開くまで返らない。
        // 購読はその後になるため、溜めて配り直さないと起動直後のログが丸ごと失われる
        await using var harness = await TunnelHarness.CreateAsync();
        var plan = harness.PlanForRemoteHost();

        var launched = await harness.Launcher.LaunchAsync(plan);
        await Assert.That(launched.IsSuccess).IsTrue();
        await using var handle = launched.Value;

        List<string> lines = [];
        handle.LogEmitted += (_, e) => lines.Add(e.Line);

        await WaitUntilAsync(() => lines.Count > 0);
        await Assert.That(lines[0]).Contains($"Port {plan.LocalPort.Value} opened");
    }

    [Test]
    public async Task ポートを開かずに終了すると起動失敗になる()
    {
        await using var harness = await TunnelHarness.CreateAsync(mode: "exit-before-open");
        var plan = harness.PlanForRemoteHost();

        var launched = await harness.Launcher.LaunchAsync(plan);

        await Assert.That(launched.IsFailure).IsTrue();
        await Assert.That(launched.Error.Phase).IsEqualTo(FailurePhase.Plugin);
        await Assert.That(launched.Error.Code).IsEqualTo("PluginExited");
    }

    [Test]
    public async Task 開通メッセージを出さなくてもポートが開けば確立とみなす()
    {
        // 本物の plugin の出力文言に依存しないことの確認
        await using var harness = await TunnelHarness.CreateAsync(quiet: true);
        var plan = harness.PlanForRemoteHost();

        var launched = await harness.Launcher.LaunchAsync(plan);

        await Assert.That(launched.IsSuccess).IsTrue();
        await using var handle = launched.Value;
        await Assert.That(harness.PortProbe.IsListening(plan.LocalPort)).IsTrue();
    }

    [Test]
    public async Task pluginが見つからなければインストール案内のエラーになる()
    {
        await using var harness = await TunnelHarness.CreateAsync();
        var locator = new Infrastructure.Tunnel.SessionManagerPluginLocator(
            () => Path.Combine(Path.GetTempPath(), "shionji-missing-plugin.exe"));
        var launcher = new Infrastructure.Tunnel.SessionManagerPluginLauncher(
            new Infrastructure.Aws.AwsClientFactory(harness.Aws.Url), locator, harness.PortProbe);

        var launched = await launcher.LaunchAsync(harness.PlanForRemoteHost());

        await Assert.That(launched.IsFailure).IsTrue();
        await Assert.That(launched.Error.Code).IsEqualTo("PluginNotFound");
        // plugin を探す前に AWS を呼ばない
        await Assert.That(harness.Aws.Received("StartSession")).IsFalse();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("条件が時間内に満たされませんでした。");
            await Task.Delay(50);
        }
    }
}
