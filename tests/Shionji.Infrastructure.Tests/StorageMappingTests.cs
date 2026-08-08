using System.Text.Json;
using Shionji.Domain.Configuration;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Storage;

namespace Shionji.Infrastructure.Tests;

public class StorageMappingTests
{
    private static ForwardingConfig Build(
        LocalPortSpec localPort,
        Destination destination,
        GatewaySpec gateway) =>
        ForwardingConfig.Create(
            ConfigId.New(),
            ConfigName.Create("round-trip").Value,
            new AwsContext(ProfileName.Create("dev").Value, AwsRegion.Create("ap-northeast-1").Value),
            localPort,
            destination,
            gateway,
            new ConfigOptions(AutoReconnect: true, ConnectOnLaunch: true)).Value;

    private static async Task AssertRoundTrip(ForwardingConfig original)
    {
        // JSON を経由しても情報が落ちないことまで確認する
        var json = JsonSerializer.Serialize(StorageMapping.ToDto(original));
        var dto = JsonSerializer.Deserialize<ConfigDto>(json)!;
        var restored = StorageMapping.ToDomain(dto);

        await Assert.That(restored.IsSuccess).IsTrue();
        await Assert.That(restored.Value).IsEqualTo(original);
    }

    [Test]
    public async Task 直接指定とインスタンスID踏み台のラウンドトリップ()
    {
        await AssertRoundTrip(Build(
            new LocalPortSpec.Fixed(Port.Create(15432).Value),
            new Destination.Static(HostName.Create("db.example.internal").Value, Port.Create(5432).Value),
            new GatewaySpec.Ec2(new Ec2Selector.ById(InstanceId.Create("i-0123456789abcdef0").Value))));
    }

    [Test]
    public async Task ElastiCacheクエリとEC2クエリ踏み台のラウンドトリップ()
    {
        var tags = TagFilters.Of(
            TagFilter.Create("Environment", ["production", "staging"]).Value,
            TagFilter.Create("Team", ["platform"]).Value);

        await AssertRoundTrip(Build(
            LocalPortSpec.Auto.Instance,
            new Destination.Query(
                new ElastiCacheQuery(
                    NamePattern.Create("prod-redis*").Value, tags, MatchPolicy.PickFirst, CacheEndpointRole.Reader),
                PortSelection.FromResource.Instance),
            new GatewaySpec.Ec2(new Ec2Selector.ByQuery(
                new Ec2Query(NamePattern.Create("bastion-*").Value, TagFilters.Empty, MatchPolicy.RequireSingle)))));
    }

    [Test]
    public async Task AuroraクエリとECS踏み台のラウンドトリップ()
    {
        await AssertRoundTrip(Build(
            new LocalPortSpec.Fixed(Port.Create(13306).Value),
            new Destination.Query(
                new AuroraQuery(null, TagFilters.Empty, MatchPolicy.RequireSingle, AuroraEndpointRole.Reader),
                new PortSelection.Explicit(Port.Create(3306).Value)),
            new GatewaySpec.Ecs(
                ClusterName.Create("prod-cluster").Value,
                ServiceName.Create("proxy").Value,
                ContainerName.Create("app").Value)));
    }

    [Test]
    public async Task ECSタスク転送先と経路Directのラウンドトリップ()
    {
        await AssertRoundTrip(Build(
            new LocalPortSpec.Fixed(Port.Create(18080).Value),
            new Destination.Query(
                new EcsTaskQuery(
                    ClusterName.Create("prod-cluster").Value,
                    ServiceName.Create("api").Value,
                    null,
                    MatchPolicy.PickFirst),
                new PortSelection.Explicit(Port.Create(8080).Value)),
            GatewaySpec.Direct.Instance));
    }

    [Test]
    public async Task 不正なDTOは失敗として報告される()
    {
        var dto = new ConfigDto
        {
            Id = Guid.NewGuid(),
            Name = "",
            Profile = "dev",
            Region = "ap-northeast-1",
            Destination = new StaticDestinationDto { Host = "h.example.com", Port = 5432 },
            Gateway = new DirectGatewayDto(),
        };

        var result = StorageMapping.ToDomain(dto);

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task EC2踏み台にIDもクエリも無ければ失敗する()
    {
        var dto = new ConfigDto
        {
            Id = Guid.NewGuid(),
            Name = "x",
            Profile = "dev",
            Region = "ap-northeast-1",
            Destination = new StaticDestinationDto { Host = "h.example.com", Port = 5432 },
            Gateway = new Ec2GatewayDto(),
        };

        var result = StorageMapping.ToDomain(dto);

        await Assert.That(result.IsFailure).IsTrue();
    }
}
