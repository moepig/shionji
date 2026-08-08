using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;

namespace Shionji.Domain.Tests;

public class TunnelPlannerTests
{
    private static readonly Domain.ValueObjects.Port Local = TestData.Port(15432);

    private static Destination StaticDest(string host = "db.example.internal", int port = 5432) =>
        new Destination.Static(TestData.Host(host), TestData.Port(port));

    private static Destination QueryDest(ResourceQuery query, PortSelection? port = null) =>
        new Destination.Query(query, port ?? new PortSelection.Explicit(TestData.Port(5432)));

    // --- 直接指定 + 踏み台 ---

    [Test]
    public async Task 直接指定とインスタンスID踏み台はRemoteHost転送になる()
    {
        var config = TestData.Config(StaticDest(), TestData.Ec2GatewayById("i-0123456789abcdef0"));

        var result = TunnelPlanner.CreatePlan(config, null, null, Local);

        await Assert.That(result.IsSuccess).IsTrue();
        var plan = result.Value;
        await Assert.That(plan.Target.Value).IsEqualTo("i-0123456789abcdef0");
        await Assert.That(plan.LocalPort).IsEqualTo(Local);
        var mode = (SessionMode.RemoteHostForward)plan.Mode;
        await Assert.That(mode.Host.Value).IsEqualTo("db.example.internal");
        await Assert.That(mode.RemotePort.Value).IsEqualTo(5432);
        await Assert.That(mode.DocumentName).IsEqualTo("AWS-StartPortForwardingSessionToRemoteHost");
    }

    [Test]
    public async Task ElastiCacheクエリの既定ポートがRemoteHost転送に使われる()
    {
        var config = TestData.Config(
            QueryDest(TestData.QueryCache(), PortSelection.FromResource.Instance),
            TestData.Ec2GatewayByQuery());
        var destination = TestData.Resource(host: "redis.prod.cache.amazonaws.com", defaultPort: 6379);
        var gateway = TestData.Resource(host: "10.0.1.5", ssmTarget: "i-0aaaaaaaaaaaaaaaa");

        var result = TunnelPlanner.CreatePlan(config, destination, gateway, Local);

        await Assert.That(result.IsSuccess).IsTrue();
        var mode = (SessionMode.RemoteHostForward)result.Value.Mode;
        await Assert.That(result.Value.Target.Value).IsEqualTo("i-0aaaaaaaaaaaaaaaa");
        await Assert.That(mode.Host.Value).IsEqualTo("redis.prod.cache.amazonaws.com");
        await Assert.That(mode.RemotePort.Value).IsEqualTo(6379);
    }

    [Test]
    public async Task Auroraクエリの明示ポートがECS踏み台経由で使われる()
    {
        var config = TestData.Config(
            QueryDest(TestData.QueryAurora(), new PortSelection.Explicit(TestData.Port(3306))),
            TestData.EcsGateway());
        var destination = TestData.Resource(host: "cluster.rds.amazonaws.com", defaultPort: 5432);
        var gateway = TestData.Resource(host: null, ssmTarget: "ecs:prod_task1_runtime1");

        var result = TunnelPlanner.CreatePlan(config, destination, gateway, Local);

        await Assert.That(result.IsSuccess).IsTrue();
        var mode = (SessionMode.RemoteHostForward)result.Value.Mode;
        await Assert.That(result.Value.Target.Value).IsEqualTo("ecs:prod_task1_runtime1");
        await Assert.That(mode.RemotePort.Value).IsEqualTo(3306);
    }

    // --- EC2 / ECS 転送先 + Direct ---

    [Test]
    public async Task EC2転送先の経路DirectはDirect転送になる()
    {
        var config = TestData.Config(QueryDest(TestData.QueryEc2()), GatewaySpec.Direct.Instance);
        var destination = TestData.Resource(host: "10.0.2.10", ssmTarget: "i-0bbbbbbbbbbbbbbbb");

        var result = TunnelPlanner.CreatePlan(config, destination, null, Local);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value.Target.Value).IsEqualTo("i-0bbbbbbbbbbbbbbbb");
        var mode = (SessionMode.DirectForward)result.Value.Mode;
        await Assert.That(mode.RemotePort.Value).IsEqualTo(5432);
        await Assert.That(mode.DocumentName).IsEqualTo("AWS-StartPortForwardingSession");
    }

    [Test]
    public async Task ECSタスク転送先の経路DirectはDirect転送になる()
    {
        var config = TestData.Config(QueryDest(TestData.QueryEcsTask()), GatewaySpec.Direct.Instance);
        var destination = TestData.Resource(host: "10.0.3.20", ssmTarget: "ecs:prod_task9_runtime9");

        var result = TunnelPlanner.CreatePlan(config, destination, null, Local);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value.Target.Value).IsEqualTo("ecs:prod_task9_runtime9");
        await Assert.That(result.Value.Mode).IsTypeOf<SessionMode.DirectForward>();
    }

    [Test]
    public async Task EC2転送先を踏み台経由にするとプライベートIPへのRemoteHost転送になる()
    {
        var config = TestData.Config(QueryDest(TestData.QueryEc2()), TestData.Ec2GatewayById("i-0123456789abcdef0"));
        var destination = TestData.Resource(host: "10.0.2.10", ssmTarget: "i-0bbbbbbbbbbbbbbbb");

        var result = TunnelPlanner.CreatePlan(config, destination, null, Local);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value.Target.Value).IsEqualTo("i-0123456789abcdef0");
        var mode = (SessionMode.RemoteHostForward)result.Value.Mode;
        await Assert.That(mode.Host.Value).IsEqualTo("10.0.2.10");
    }

    // --- エラーパス ---

    [Test]
    public async Task クエリ転送先が未解決なら失敗する()
    {
        var config = TestData.Config(QueryDest(TestData.QueryCache()), TestData.Ec2GatewayById());

        var result = TunnelPlanner.CreatePlan(config, null, null, Local);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Phase).IsEqualTo(FailurePhase.ResolveDestination);
        await Assert.That(result.Error.Code).IsEqualTo("DestinationNotResolved");
    }

    [Test]
    public async Task 既定ポート指定でリソースに既定ポートがなければ失敗する()
    {
        var config = TestData.Config(
            QueryDest(TestData.QueryCache(), PortSelection.FromResource.Instance),
            TestData.Ec2GatewayById());
        var destination = TestData.Resource(host: "redis.example.com", defaultPort: null);

        var result = TunnelPlanner.CreatePlan(config, destination, null, Local);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Code).IsEqualTo("NoDefaultPort");
    }

    [Test]
    public async Task Direct転送でSSMターゲットを持たないリソースは失敗する()
    {
        var config = TestData.Config(QueryDest(TestData.QueryEc2()), GatewaySpec.Direct.Instance);
        var destination = TestData.Resource(host: "10.0.2.10", ssmTarget: null);

        var result = TunnelPlanner.CreatePlan(config, destination, null, Local);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Code).IsEqualTo("NotSessionCapable");
    }

    [Test]
    public async Task クエリ踏み台が未解決なら失敗する()
    {
        var config = TestData.Config(StaticDest(), TestData.Ec2GatewayByQuery());

        var result = TunnelPlanner.CreatePlan(config, null, null, Local);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Phase).IsEqualTo(FailurePhase.ResolveGateway);
        await Assert.That(result.Error.Code).IsEqualTo("GatewayNotResolved");
    }

    [Test]
    public async Task 踏み台リソースがSSMターゲットを持たなければ失敗する()
    {
        var config = TestData.Config(StaticDest(), TestData.EcsGateway());
        var gateway = TestData.Resource(ssmTarget: null);

        var result = TunnelPlanner.CreatePlan(config, null, gateway, Local);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Phase).IsEqualTo(FailurePhase.ResolveGateway);
        await Assert.That(result.Error.Code).IsEqualTo("NotSessionCapable");
    }

    [Test]
    public async Task RemoteHost転送で転送先エンドポイントが無ければ失敗する()
    {
        var config = TestData.Config(QueryDest(TestData.QueryEc2()), TestData.Ec2GatewayById());
        var destination = TestData.Resource(host: null, ssmTarget: "i-0bbbbbbbbbbbbbbbb");

        var result = TunnelPlanner.CreatePlan(config, destination, null, Local);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Code).IsEqualTo("NoEndpoint");
    }
}
