using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.ElastiCache;
using Amazon.ElastiCache.Model;
using Amazon.RDS;
using Amazon.RDS.Model;
using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Aws;

/// <summary>AWS API を呼び出す IResourceCatalog 実装。名前 glob はドメインの NamePattern で客側フィルタする。</summary>
public sealed class AwsResourceCatalog(AwsClientFactory clientFactory, IClock clock) : IResourceCatalog
{
    public async Task<ResolutionOutcome> ResolveAsync(
        AwsContext aws, ResourceQuery query, FailurePhase phase, CancellationToken cancellationToken = default)
    {
        try
        {
            return query switch
            {
                ElastiCacheQuery q => await ResolveElastiCacheAsync(aws, q, cancellationToken),
                AuroraQuery q => await ResolveAuroraAsync(aws, q, cancellationToken),
                Ec2Query q => await ResolveEc2Async(aws, q, cancellationToken),
                EcsTaskQuery q => await ResolveEcsAsync(aws, q, phase, cancellationToken),
                _ => throw new InvalidOperationException($"未知のクエリ型: {query.GetType()}"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FailedResolutionException ex)
        {
            return new ResolutionOutcome.Failed(ex.Error);
        }
        catch (Exception ex)
        {
            return new ResolutionOutcome.Failed(
                AwsErrors.Classify(ex, phase, aws.Profile, clientFactory.IsSsoProfile(aws.Profile)));
        }
    }

    /// <summary>資格情報エラーなどを例外として上位の catch に運ぶための内部例外。</summary>
    private sealed class FailedResolutionException(ErrorDetail error) : Exception(error.Message)
    {
        public ErrorDetail Error { get; } = error;
    }

    private static T Unwrap<T>(Domain.Primitives.Result<T, ErrorDetail> result) =>
        result.Match(v => v, error => throw new FailedResolutionException(error));

    private async Task<ResolutionOutcome> ResolveElastiCacheAsync(
        AwsContext aws, ElastiCacheQuery query, CancellationToken ct)
    {
        using var client = Unwrap(clientFactory.CreateElastiCache(aws));

        var groups = new List<ReplicationGroup>();
        string? marker = null;
        do
        {
            var response = await client.DescribeReplicationGroupsAsync(
                new DescribeReplicationGroupsRequest { Marker = marker }, ct);
            groups.AddRange(response.ReplicationGroups ?? []);
            marker = response.Marker;
        } while (marker is not null);

        var matching = groups.Where(g => query.MatchesName(g.ReplicationGroupId)).ToList();

        if (!query.Tags.IsEmpty)
        {
            var filtered = new List<ReplicationGroup>();
            foreach (var group in matching.Where(g => g.ARN is not null))
            {
                var tags = await client.ListTagsForResourceAsync(
                    new Amazon.ElastiCache.Model.ListTagsForResourceRequest { ResourceName = group.ARN }, ct);
                var tagMap = (tags.TagList ?? [])
                    .Where(t => t.Key is not null)
                    .ToDictionary(t => t.Key, t => t.Value ?? string.Empty, StringComparer.Ordinal);
                if (query.MatchesTags(tagMap))
                    filtered.Add(group);
            }

            matching = filtered;
        }

        var candidates = matching
            .Select(g => ElastiCacheMapping.ToResource(g, query.Role, clock.UtcNow))
            .OfType<ResolvedResource>()
            .ToList();
        return CandidateSelection.Apply(query.Match, candidates);
    }

    private async Task<ResolutionOutcome> ResolveAuroraAsync(
        AwsContext aws, AuroraQuery query, CancellationToken ct)
    {
        using var client = Unwrap(clientFactory.CreateRds(aws));

        var clusters = new List<DBCluster>();
        string? marker = null;
        do
        {
            var response = await client.DescribeDBClustersAsync(
                new DescribeDBClustersRequest { Marker = marker }, ct);
            clusters.AddRange(response.DBClusters ?? []);
            marker = response.Marker;
        } while (marker is not null);

        var candidates = clusters
            .Where(c => query.MatchesName(c.DBClusterIdentifier))
            .Where(c => query.MatchesTags(AuroraMapping.TagsOf(c)))
            .Select(c => AuroraMapping.ToResource(c, query.Role, clock.UtcNow))
            .OfType<ResolvedResource>()
            .ToList();
        return CandidateSelection.Apply(query.Match, candidates);
    }

    private async Task<ResolutionOutcome> ResolveEc2Async(
        AwsContext aws, Ec2Query query, CancellationToken ct)
    {
        using var client = Unwrap(clientFactory.CreateEc2(aws));

        var request = new DescribeInstancesRequest
        {
            Filters = [new Amazon.EC2.Model.Filter("instance-state-name", ["running"])],
        };
        foreach (var tagFilter in query.Tags.Items)
            request.Filters.Add(new Amazon.EC2.Model.Filter($"tag:{tagFilter.Key}", [.. tagFilter.Values]));

        var instances = new List<Instance>();
        string? token = null;
        do
        {
            request.NextToken = token;
            var response = await client.DescribeInstancesAsync(request, ct);
            instances.AddRange((response.Reservations ?? []).SelectMany(r => r.Instances ?? []));
            token = response.NextToken;
        } while (token is not null);

        var candidates = instances
            .Where(i => query.MatchesName(Ec2Mapping.NameOf(i)))
            .Select(i => Ec2Mapping.ToResource(i, clock.UtcNow))
            .ToList();
        return CandidateSelection.Apply(query.Match, candidates);
    }

    private async Task<ResolutionOutcome> ResolveEcsAsync(
        AwsContext aws, EcsTaskQuery query, FailurePhase phase, CancellationToken ct)
    {
        using var client = Unwrap(clientFactory.CreateEcs(aws));

        var taskArns = new List<string>();
        string? token = null;
        do
        {
            var response = await client.ListTasksAsync(
                new ListTasksRequest
                {
                    Cluster = query.Cluster.Value,
                    ServiceName = query.Service?.Value,
                    DesiredStatus = DesiredStatus.RUNNING,
                    NextToken = token,
                }, ct);
            taskArns.AddRange(response.TaskArns ?? []);
            token = response.NextToken;
        } while (token is not null);

        if (taskArns.Count == 0)
            return ResolutionOutcome.NotFound.Instance;

        var tasks = new List<Amazon.ECS.Model.Task>();
        foreach (var chunk in taskArns.Chunk(100))
        {
            var described = await client.DescribeTasksAsync(
                new DescribeTasksRequest { Cluster = query.Cluster.Value, Tasks = [.. chunk] }, ct);
            tasks.AddRange(described.Tasks ?? []);
        }

        var candidates = tasks
            .Select(t => EcsMapping.ToResource(query.Cluster, t, query.Container, clock.UtcNow))
            .OfType<ResolvedResource>()
            .ToList();

        var outcome = CandidateSelection.Apply(query.Match, candidates);

        // 踏み台として使う場合、RuntimeId の無いタスク (ECS Exec 無効) は SSM セッションを張れない
        if (phase == FailurePhase.ResolveGateway &&
            outcome is ResolutionOutcome.Resolved { Resource.SsmTarget: null } resolved)
        {
            return new ResolutionOutcome.Failed(new ErrorDetail(
                phase,
                "EcsExecUnavailable",
                $"タスク「{resolved.Resource.DisplayName}」の RuntimeId を取得できません。" +
                "ECS Exec (enableExecuteCommand) が有効か確認してください。"));
        }

        return outcome;
    }
}
