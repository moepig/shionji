using Shionji.Domain.Configuration;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Aws;

namespace Shionji.Infrastructure.Tests;

public class ElastiCacheMappingTests
{
    private static Amazon.ElastiCache.Model.ReplicationGroup Group() => new()
    {
        ReplicationGroupId = "prod-redis",
        ARN = "arn:aws:elasticache:apne1:123:replicationgroup:prod-redis",
        NodeGroups =
        [
            new Amazon.ElastiCache.Model.NodeGroup
            {
                PrimaryEndpoint = new Amazon.ElastiCache.Model.Endpoint { Address = "primary.cache.example", Port = 6379 },
                ReaderEndpoint = new Amazon.ElastiCache.Model.Endpoint { Address = "reader.cache.example", Port = 6380 },
            },
        ],
    };

    [Test]
    public async Task ロールに応じたエンドポイントを選ぶ()
    {
        var primary = ElastiCacheMapping.ToResource(Group(), CacheEndpointRole.Primary, DateTimeOffset.UnixEpoch)!;
        var reader = ElastiCacheMapping.ToResource(Group(), CacheEndpointRole.Reader, DateTimeOffset.UnixEpoch)!;

        await Assert.That(primary.Host!.Value).IsEqualTo("primary.cache.example");
        await Assert.That(primary.DefaultPort!.Value).IsEqualTo(6379);
        await Assert.That(reader.Host!.Value).IsEqualTo("reader.cache.example");
        await Assert.That(reader.DefaultPort!.Value).IsEqualTo(6380);
        await Assert.That(primary.SsmTarget).IsNull();
    }

    [Test]
    public async Task 対応するエンドポイントが無ければ候補にならない()
    {
        var resource = ElastiCacheMapping.ToResource(
            Group(), CacheEndpointRole.Configuration, DateTimeOffset.UnixEpoch);
        await Assert.That(resource).IsNull();
    }
}

public class AuroraMappingTests
{
    private static Amazon.RDS.Model.DBCluster Cluster() => new()
    {
        DBClusterIdentifier = "prod-aurora",
        DBClusterArn = "arn:aws:rds:apne1:123:cluster:prod-aurora",
        Endpoint = "writer.rds.example",
        ReaderEndpoint = "reader.rds.example",
        Port = 3306,
        TagList = [new Amazon.RDS.Model.Tag { Key = "Environment", Value = "production" }],
    };

    [Test]
    public async Task WriterとReaderのエンドポイントを選べる()
    {
        var writer = AuroraMapping.ToResource(Cluster(), AuroraEndpointRole.Writer, DateTimeOffset.UnixEpoch)!;
        var reader = AuroraMapping.ToResource(Cluster(), AuroraEndpointRole.Reader, DateTimeOffset.UnixEpoch)!;

        await Assert.That(writer.Host!.Value).IsEqualTo("writer.rds.example");
        await Assert.That(reader.Host!.Value).IsEqualTo("reader.rds.example");
        await Assert.That(writer.DefaultPort!.Value).IsEqualTo(3306);
    }

    [Test]
    public async Task タグは辞書に変換される()
    {
        var tags = AuroraMapping.TagsOf(Cluster());
        await Assert.That(tags["Environment"]).IsEqualTo("production");
    }
}

public class Ec2MappingTests
{
    [Test]
    public async Task Nameタグを表示名とし無ければインスタンスIDを使う()
    {
        var named = new Amazon.EC2.Model.Instance
        {
            InstanceId = "i-0aaa0000000000001",
            PrivateIpAddress = "10.0.1.5",
            Tags = [new Amazon.EC2.Model.Tag { Key = "Name", Value = "bastion-01" }],
        };
        var unnamed = new Amazon.EC2.Model.Instance { InstanceId = "i-0bbb0000000000002" };

        await Assert.That(Ec2Mapping.NameOf(named)).IsEqualTo("bastion-01");
        await Assert.That(Ec2Mapping.NameOf(unnamed)).IsEqualTo("i-0bbb0000000000002");

        var resource = Ec2Mapping.ToResource(named, DateTimeOffset.UnixEpoch);
        await Assert.That(resource.Host!.Value).IsEqualTo("10.0.1.5");
        await Assert.That(resource.SsmTarget!.Value).IsEqualTo("i-0aaa0000000000001");
        await Assert.That(resource.DefaultPort).IsNull();
    }
}

public class EcsMappingTests
{
    private static Amazon.ECS.Model.Task Task(string? runtimeId = "runtime-1") => new()
    {
        TaskArn = "arn:aws:ecs:apne1:123:task/prod-cluster/abc123",
        Containers =
        [
            new Amazon.ECS.Model.Container
            {
                Name = "app",
                RuntimeId = runtimeId,
                NetworkInterfaces =
                [
                    new Amazon.ECS.Model.NetworkInterface { PrivateIpv4Address = "10.0.3.21" },
                ],
            },
        ],
    };

    private static ClusterName Cluster() => ClusterName.Create("prod-cluster").Value;

    [Test]
    public async Task タスクはECS形式のSSMターゲットに変換される()
    {
        var resource = EcsMapping.ToResource(Cluster(), Task(), null, DateTimeOffset.UnixEpoch)!;

        await Assert.That(resource.DisplayName).IsEqualTo("abc123");
        await Assert.That(resource.Host!.Value).IsEqualTo("10.0.3.21");
        await Assert.That(resource.SsmTarget!.Value).IsEqualTo("ecs:prod-cluster_abc123_runtime-1");
    }

    [Test]
    public async Task RuntimeIdが無ければSSMターゲットを持たない()
    {
        var resource = EcsMapping.ToResource(Cluster(), Task(runtimeId: null), null, DateTimeOffset.UnixEpoch)!;
        await Assert.That(resource.SsmTarget).IsNull();
    }

    [Test]
    public async Task 指定コンテナが無いタスクは候補にならない()
    {
        var resource = EcsMapping.ToResource(
            Cluster(), Task(), ContainerName.Create("sidecar").Value, DateTimeOffset.UnixEpoch);
        await Assert.That(resource).IsNull();
    }
}
