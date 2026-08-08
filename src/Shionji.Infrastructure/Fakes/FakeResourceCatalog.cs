using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Fakes;

/// <summary>
/// デモモード / テスト用の IResourceCatalog。AWS を呼ばずにそれらしいリソースを返す。
/// 名前パターンに埋め込んだキーワードで挙動を再現できる:
/// ambiguous → 複数一致、notfound → 見つからない、denied → 権限エラー。
/// プロファイル名 expired-sso は SSO トークン切れを再現する。
/// </summary>
public sealed class FakeResourceCatalog(IClock clock) : IResourceCatalog
{
    public async Task<ResolutionOutcome> ResolveAsync(
        AwsContext aws, ResourceQuery query, FailurePhase phase, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Random.Shared.Next(400, 900), cancellationToken);

        if (aws.Profile.Value == "expired-sso")
        {
            return new ResolutionOutcome.Failed(new ErrorDetail(
                FailurePhase.Credentials,
                "SsoLoginRequired",
                $"プロファイル「{aws.Profile.Value}」の認証情報が期限切れです。" +
                $"`aws sso login --profile {aws.Profile.Value}` を実行してください。"));
        }

        var pattern = query.Name?.Value ?? string.Empty;
        if (pattern.Contains("notfound", StringComparison.OrdinalIgnoreCase))
            return ResolutionOutcome.NotFound.Instance;

        if (pattern.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolutionOutcome.Failed(new ErrorDetail(
                FailurePhase.Permission, "AccessDenied", "AWS API の権限が不足しています (デモ)。"));
        }

        var baseName = BaseName(pattern, query);
        if (pattern.Contains("ambiguous", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolutionOutcome.Ambiguous(
                [.. Enumerable.Range(1, 3).Select(i => Build(query, $"{baseName}-{i:00}"))]);
        }

        return new ResolutionOutcome.Resolved(Build(query, baseName));
    }

    private ResolvedResource Build(ResourceQuery query, string name) => query switch
    {
        ElastiCacheQuery => new ResolvedResource(
            new ResourceId($"arn:aws:elasticache:demo:{name}"),
            name,
            Host($"{name}.demo.cache.amazonaws.com"),
            Port(6379),
            null,
            clock.UtcNow),
        AuroraQuery => new ResolvedResource(
            new ResourceId($"arn:aws:rds:demo:{name}"),
            name,
            Host($"{name}.cluster-demo.ap-northeast-1.rds.amazonaws.com"),
            Port(3306),
            null,
            clock.UtcNow),
        Ec2Query => new ResolvedResource(
            new ResourceId("i-0demo0123456789a"),
            name,
            Host($"10.0.2.{Math.Abs(name.GetHashCode()) % 200 + 10}"),
            null,
            new SsmTargetId("i-0demo0123456789a"),
            clock.UtcNow),
        EcsTaskQuery ecs => new ResolvedResource(
            new ResourceId($"arn:aws:ecs:demo:task/{name}"),
            name,
            Host("10.0.3.21"),
            null,
            new SsmTargetId($"ecs:{ecs.Cluster.Value}_demotask_demoruntime"),
            clock.UtcNow),
        _ => throw new InvalidOperationException($"未知のクエリ型: {query.GetType()}"),
    };

    private static string BaseName(string pattern, ResourceQuery query)
    {
        var cleaned = pattern.Replace("*", string.Empty).Replace("?", string.Empty).TrimEnd('-', '.');
        if (cleaned.Length > 0)
            return cleaned;
        return query is EcsTaskQuery ecs ? $"{ecs.Cluster.Value}-task" : "demo-resource";
    }

    private static HostName Host(string value) => HostName.Create(value).Value;

    private static Port Port(int value) => Domain.ValueObjects.Port.Create(value).Value;
}
