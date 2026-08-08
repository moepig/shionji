using System.Text.Json;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Tunnel;

namespace Shionji.Infrastructure.Tests;

public class PluginArgumentsTests
{
    private static TunnelPlan Plan(SessionMode mode) =>
        new(
            new AwsContext(ProfileName.Create("dev").Value, AwsRegion.Create("ap-northeast-1").Value),
            new SsmTargetId("i-0123456789abcdef0"),
            mode,
            Port.Create(15432).Value);

    [Test]
    public async Task Direct転送のパラメータ()
    {
        var parameters = PluginArguments.BuildParameters(
            new SessionMode.DirectForward(Port.Create(22).Value), Port.Create(12222).Value);

        await Assert.That(parameters["portNumber"]).IsEquivalentTo(["22"]);
        await Assert.That(parameters["localPortNumber"]).IsEquivalentTo(["12222"]);
        await Assert.That(parameters.ContainsKey("host")).IsFalse();
    }

    [Test]
    public async Task RemoteHost転送のパラメータ()
    {
        var parameters = PluginArguments.BuildParameters(
            new SessionMode.RemoteHostForward(HostName.Create("db.example.internal").Value, Port.Create(5432).Value),
            Port.Create(15432).Value);

        await Assert.That(parameters["host"]).IsEquivalentTo(["db.example.internal"]);
        await Assert.That(parameters["portNumber"]).IsEquivalentTo(["5432"]);
        await Assert.That(parameters["localPortNumber"]).IsEquivalentTo(["15432"]);
    }

    [Test]
    public async Task 引数列はAWSCLIと同じ順序になる()
    {
        var plan = Plan(new SessionMode.RemoteHostForward(
            HostName.Create("db.example.internal").Value, Port.Create(5432).Value));

        var args = PluginArguments.Build(plan, "session-1", "token-1", "wss://stream.example");

        await Assert.That(args.Length).IsEqualTo(6);

        using var sessionJson = JsonDocument.Parse(args[0]);
        await Assert.That(sessionJson.RootElement.GetProperty("SessionId").GetString()).IsEqualTo("session-1");
        await Assert.That(sessionJson.RootElement.GetProperty("TokenValue").GetString()).IsEqualTo("token-1");
        await Assert.That(sessionJson.RootElement.GetProperty("StreamUrl").GetString()).IsEqualTo("wss://stream.example");

        await Assert.That(args[1]).IsEqualTo("ap-northeast-1");
        await Assert.That(args[2]).IsEqualTo("StartSession");
        await Assert.That(args[3]).IsEqualTo("dev");

        using var requestJson = JsonDocument.Parse(args[4]);
        await Assert.That(requestJson.RootElement.GetProperty("Target").GetString()).IsEqualTo("i-0123456789abcdef0");
        await Assert.That(requestJson.RootElement.GetProperty("DocumentName").GetString())
            .IsEqualTo("AWS-StartPortForwardingSessionToRemoteHost");
        await Assert.That(requestJson.RootElement.GetProperty("Parameters").GetProperty("host")[0].GetString())
            .IsEqualTo("db.example.internal");

        await Assert.That(args[5]).IsEqualTo("https://ssm.ap-northeast-1.amazonaws.com");
    }
}
