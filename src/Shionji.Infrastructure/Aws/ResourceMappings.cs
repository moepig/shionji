using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Aws;

/// <summary>AWS SDK モデル → ドメインの ResolvedResource への純粋なマッピング群。</summary>
public static class ElastiCacheMapping
{
    /// <returns>Role に対応するエンドポイントを持たないグループは null (候補から除外)。</returns>
    public static ResolvedResource? ToResource(
        Amazon.ElastiCache.Model.ReplicationGroup group,
        CacheEndpointRole role,
        DateTimeOffset now)
    {
        var nodeGroup = (group.NodeGroups ?? []).FirstOrDefault();
        var endpoint = role switch
        {
            CacheEndpointRole.Primary => nodeGroup?.PrimaryEndpoint,
            CacheEndpointRole.Reader => nodeGroup?.ReaderEndpoint,
            CacheEndpointRole.Configuration => group.ConfigurationEndpoint,
            _ => null,
        };

        if (endpoint?.Address is not { Length: > 0 } address)
            return null;

        return new ResolvedResource(
            new ResourceId(group.ARN ?? group.ReplicationGroupId),
            group.ReplicationGroupId,
            HostName.Create(address).Match(h => h, _ => null!),
            endpoint.Port is { } port ? Port.Create(port).Match(p => p, _ => null!) : null,
            SsmTarget: null,
            now);
    }
}

public static class AuroraMapping
{
    public static ResolvedResource? ToResource(
        Amazon.RDS.Model.DBCluster cluster,
        AuroraEndpointRole role,
        DateTimeOffset now)
    {
        var address = role switch
        {
            AuroraEndpointRole.Writer => cluster.Endpoint,
            AuroraEndpointRole.Reader => cluster.ReaderEndpoint,
            _ => null,
        };

        if (address is not { Length: > 0 })
            return null;

        return new ResolvedResource(
            new ResourceId(cluster.DBClusterArn ?? cluster.DBClusterIdentifier),
            cluster.DBClusterIdentifier,
            HostName.Create(address).Match(h => h, _ => null!),
            cluster.Port is { } port ? Port.Create(port).Match(p => p, _ => null!) : null,
            SsmTarget: null,
            now);
    }

    public static IReadOnlyDictionary<string, string> TagsOf(Amazon.RDS.Model.DBCluster cluster) =>
        (cluster.TagList ?? [])
            .Where(t => t.Key is not null)
            .ToDictionary(t => t.Key, t => t.Value ?? string.Empty, StringComparer.Ordinal);
}

public static class Ec2Mapping
{
    public static ResolvedResource ToResource(Amazon.EC2.Model.Instance instance, DateTimeOffset now)
    {
        var name = NameOf(instance);
        return new ResolvedResource(
            new ResourceId(instance.InstanceId),
            name,
            instance.PrivateIpAddress is { Length: > 0 } ip ? HostName.Create(ip).Match(h => h, _ => null!) : null,
            DefaultPort: null,
            new SsmTargetId(instance.InstanceId),
            now);
    }

    /// <summary>Name タグがあればその値、なければインスタンス ID。名前パターンの照合対象。</summary>
    public static string NameOf(Amazon.EC2.Model.Instance instance) =>
        (instance.Tags ?? []).FirstOrDefault(t => t.Key == "Name")?.Value is { Length: > 0 } name
            ? name
            : instance.InstanceId;
}

public static class EcsMapping
{
    /// <param name="containerName">null の場合は最初のコンテナを使う。一致するコンテナが無いタスクは null。</param>
    public static ResolvedResource? ToResource(
        ClusterName cluster,
        Amazon.ECS.Model.Task task,
        ContainerName? containerName,
        DateTimeOffset now)
    {
        var containers = task.Containers ?? [];
        var container = containerName is null
            ? containers.FirstOrDefault()
            : containers.FirstOrDefault(c => c.Name == containerName.Value);
        if (container is null)
            return null;

        var taskId = TaskIdFromArn(task.TaskArn);
        var ip = (container.NetworkInterfaces ?? []).FirstOrDefault()?.PrivateIpv4Address;

        // RuntimeId が無い (ECS Exec 無効など) タスクは SSM ターゲットになれない
        var target = container.RuntimeId is { Length: > 0 } runtimeId
            ? SsmTargetId.ForEcsTask(cluster, taskId, runtimeId)
            : null;

        return new ResolvedResource(
            new ResourceId(task.TaskArn),
            taskId,
            ip is { Length: > 0 } ? HostName.Create(ip).Match(h => h, _ => null!) : null,
            DefaultPort: null,
            target,
            now);
    }

    public static string TaskIdFromArn(string taskArn)
    {
        var index = taskArn.LastIndexOf('/');
        return index >= 0 ? taskArn[(index + 1)..] : taskArn;
    }
}
