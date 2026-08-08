using Shionji.Domain.Configuration;
using Shionji.Domain.ValueObjects;
using Shionji.TestSupport;

namespace Shionji.Application.Tests;

/// <summary>
/// 経路指定から踏み台の解決クエリを導く部分の検証。
/// ここを取り違えると別のリソースへセッションを張ってしまう。
/// </summary>
public class GatewayQueryTests
{
    private static ForwardingConfig ConfigWithGateway(GatewaySpec gateway) =>
        ForwardingConfig.Create(
            ConfigId.New(),
            ConfigName.Create("gw-test").Value,
            TestData.Aws(),
            new LocalPortSpec.Fixed(TestData.Port(15432)),
            new Destination.Static(TestData.Host("db.example.internal"), TestData.Port(5432)),
            gateway,
            ConfigOptions.Default).Value;

    [Test]
    public async Task ECS踏み台はクラスタとサービスとコンテナを引き継ぐ()
    {
        var harness = new Harness();
        var config = ConfigWithGateway(new GatewaySpec.Ecs(
            ClusterName.Create("prod-cluster").Value,
            ServiceName.Create("proxy").Value,
            ContainerName.Create("sidecar").Value));

        await harness.Resolution.RefreshAsync(config);

        var query = (EcsTaskQuery)harness.Catalog.Queries.Single();
        await Assert.That(query.Cluster.Value).IsEqualTo("prod-cluster");
        await Assert.That(query.Service!.Value).IsEqualTo("proxy");
        await Assert.That(query.Container!.Value).IsEqualTo("sidecar");
        await Assert.That(query.Match).IsEqualTo(MatchPolicy.RequireSingle);
    }

    [Test]
    public async Task EC2踏み台の検索条件がそのまま使われる()
    {
        var harness = new Harness();
        var ec2Query = new Ec2Query(
            NamePattern.Create("bastion-*").Value,
            TagFilters.Of(TagFilter.Create("Env", ["prod"]).Value),
            MatchPolicy.PickFirst);
        var config = ConfigWithGateway(new GatewaySpec.Ec2(new Ec2Selector.ByQuery(ec2Query)));

        await harness.Resolution.RefreshAsync(config);

        await Assert.That(harness.Catalog.Queries.Single()).IsEqualTo(ec2Query);
    }

    [Test]
    public async Task インスタンスID指定とDirectは解決を必要としない()
    {
        var harness = new Harness();
        var byId = ConfigWithGateway(new GatewaySpec.Ec2(
            new Ec2Selector.ById(InstanceId.Create("i-0123456789abcdef0").Value)));

        await harness.Resolution.RefreshAsync(byId);

        await Assert.That(harness.Catalog.CallCount).IsEqualTo(0);
    }
}
