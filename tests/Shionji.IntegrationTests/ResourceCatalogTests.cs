using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Aws;
using Shionji.TestSupport;

namespace Shionji.IntegrationTests;

/// <summary>
/// AwsResourceCatalog を実 SDK のシリアライズ・HTTP・アンマーシャル経路ごと検証する。
/// 応答はローカルのスタブサーバが返すため AWS は不要。
/// </summary>
[NotInParallel]
public class ResourceCatalogTests
{
    private static async Task<(StubAwsServer Aws, AwsResourceCatalog Catalog, string WorkDir)> CreateAsync()
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"shionji-cat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var credentials = Path.Combine(workDir, "credentials");
        await File.WriteAllTextAsync(credentials, """
            [test]
            aws_access_key_id = AKIAIOSFODNN7EXAMPLE
            aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
            """);

        var aws = await StubAwsServer.StartAsync();
        var factory = new AwsClientFactory(endpointOverride: aws.Url, profilesLocation: credentials);
        return (aws, new AwsResourceCatalog(factory, new FakeClock()), workDir);
    }

    private static AwsContext Context() =>
        new(ProfileName.Create("test").Value, AwsRegion.Create("ap-northeast-1").Value);

    private static NamePattern Pattern(string value) => NamePattern.Create(value).Value;

    private static async Task<ResolutionOutcome> ResolveAsync(
        AwsResourceCatalog catalog, ResourceQuery query, FailurePhase phase = FailurePhase.ResolveDestination) =>
        await catalog.ResolveAsync(Context(), query, phase);

    // --- EC2 ---

    [Test]
    public async Task EC2は名前globで客側フィルタしrunningフィルタを送る()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("DescribeInstances", AwsResponses.Ec2Instances(
        [
            ("i-0aaa0000000000001", "10.0.1.5", "bastion-01"),
            ("i-0bbb0000000000002", "10.0.1.6", "app-01"),
        ]));

        var outcome = await ResolveAsync(catalog, new Ec2Query(Pattern("bastion-*"), TagFilters.Empty, MatchPolicy.RequireSingle));

        var resolved = (ResolutionOutcome.Resolved)outcome;
        await Assert.That(resolved.Resource.DisplayName).IsEqualTo("bastion-01");
        await Assert.That(resolved.Resource.Host!.Value).IsEqualTo("10.0.1.5");
        await Assert.That(resolved.Resource.SsmTarget!.Value).IsEqualTo("i-0aaa0000000000001");

        // running 絞り込みは API 側のフィルタとして送る
        var form = aws.LastRequest("DescribeInstances")!.Form;
        await Assert.That(form["Filter.1.Name"]).IsEqualTo("instance-state-name");
        await Assert.That(form["Filter.1.Value.1"]).IsEqualTo("running");

        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task EC2のタグ条件はAPIのフィルタとして送られる()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("DescribeInstances", AwsResponses.Ec2Instances([("i-0aaa0000000000001", "10.0.1.5", "bastion-01")]));

        var tags = TagFilters.Of(TagFilter.Create("Environment", ["production", "staging"]).Value);
        await ResolveAsync(catalog, new Ec2Query(null, tags, MatchPolicy.RequireSingle));

        var form = aws.LastRequest("DescribeInstances")!.Form;
        await Assert.That(form["Filter.2.Name"]).IsEqualTo("tag:Environment");
        await Assert.That(form["Filter.2.Value.1"]).IsEqualTo("production");
        await Assert.That(form["Filter.2.Value.2"]).IsEqualTo("staging");

        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task EC2はページングして全ページを集める()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("DescribeInstances", request =>
            request.Form.ContainsKey("NextToken")
                ? AwsResponses.Ec2Instances([("i-0bbb0000000000002", "10.0.1.6", "bastion-02")])
                : AwsResponses.Ec2Instances([("i-0aaa0000000000001", "10.0.1.5", "bastion-01")], nextToken: "page2"));

        var outcome = await ResolveAsync(catalog, new Ec2Query(Pattern("bastion-*"), TagFilters.Empty, MatchPolicy.RequireSingle));

        // 2 ページ分が候補になるので一意に定まらない
        var ambiguous = (ResolutionOutcome.Ambiguous)outcome;
        await Assert.That(ambiguous.Candidates.Select(c => c.DisplayName)).IsEquivalentTo(["bastion-01", "bastion-02"]);
        await Assert.That(aws.CountOf("DescribeInstances")).IsEqualTo(2);

        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task 一致しなければNotFound()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("DescribeInstances", AwsResponses.Ec2Instances([("i-0aaa0000000000001", "10.0.1.5", "app-01")]));

        var outcome = await ResolveAsync(catalog, new Ec2Query(Pattern("bastion-*"), TagFilters.Empty, MatchPolicy.RequireSingle));

        await Assert.That(outcome).IsTypeOf<ResolutionOutcome.NotFound>();
        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task 権限不足はPermissionに分類される()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("DescribeInstances", StubResponse.XmlError("UnauthorizedOperation", "権限がありません", 403));

        var outcome = await ResolveAsync(catalog, new Ec2Query(null, TagFilters.Empty, MatchPolicy.RequireSingle));

        var failed = (ResolutionOutcome.Failed)outcome;
        await Assert.That(failed.Error.Phase).IsEqualTo(FailurePhase.Permission);

        Directory.Delete(dir, recursive: true);
    }

    // --- Aurora ---

    [Test]
    public async Task Auroraはロール別エンドポイントを返しタグで客側フィルタする()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("DescribeDBClusters", AwsResponses.AuroraClusters(
        [
            ("prod-aurora", "writer.prod.rds", "reader.prod.rds", 3306, [("Environment", "production")]),
            ("stg-aurora", "writer.stg.rds", "reader.stg.rds", 3306, [("Environment", "staging")]),
        ]));

        var tags = TagFilters.Of(TagFilter.Create("Environment", ["production"]).Value);
        var outcome = await ResolveAsync(
            catalog, new AuroraQuery(null, tags, MatchPolicy.RequireSingle, AuroraEndpointRole.Reader));

        var resolved = (ResolutionOutcome.Resolved)outcome;
        await Assert.That(resolved.Resource.DisplayName).IsEqualTo("prod-aurora");
        await Assert.That(resolved.Resource.Host!.Value).IsEqualTo("reader.prod.rds");
        await Assert.That(resolved.Resource.DefaultPort!.Value).IsEqualTo(3306);

        Directory.Delete(dir, recursive: true);
    }

    // --- ElastiCache ---

    [Test]
    public async Task ElastiCacheはPrimaryエンドポイントを返す()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("DescribeReplicationGroups", AwsResponses.ElastiCacheGroups(
            [("prod-redis", "primary.prod.cache", "reader.prod.cache", 6379)]));

        var outcome = await ResolveAsync(
            catalog, new ElastiCacheQuery(Pattern("prod-*"), TagFilters.Empty, MatchPolicy.RequireSingle, CacheEndpointRole.Primary));

        var resolved = (ResolutionOutcome.Resolved)outcome;
        await Assert.That(resolved.Resource.Host!.Value).IsEqualTo("primary.prod.cache");
        await Assert.That(resolved.Resource.DefaultPort!.Value).IsEqualTo(6379);
        // タグ条件がなければタグ取得の追加呼び出しはしない
        await Assert.That(aws.Received("ListTagsForResource")).IsFalse();

        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task ElastiCacheのタグ条件はListTagsForResourceで絞り込む()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("DescribeReplicationGroups", AwsResponses.ElastiCacheGroups(
        [
            ("redis-a", "a.cache", "a-ro.cache", 6379),
            ("redis-b", "b.cache", "b-ro.cache", 6379),
        ]));
        aws.On("ListTagsForResource", request =>
            request.Form["ResourceName"].EndsWith("redis-a", StringComparison.Ordinal)
                ? AwsResponses.ElastiCacheTags(("Environment", "production"))
                : AwsResponses.ElastiCacheTags(("Environment", "staging")));

        var tags = TagFilters.Of(TagFilter.Create("Environment", ["production"]).Value);
        var outcome = await ResolveAsync(
            catalog, new ElastiCacheQuery(null, tags, MatchPolicy.RequireSingle, CacheEndpointRole.Primary));

        var resolved = (ResolutionOutcome.Resolved)outcome;
        await Assert.That(resolved.Resource.DisplayName).IsEqualTo("redis-a");
        await Assert.That(aws.CountOf("ListTagsForResource")).IsEqualTo(2);

        Directory.Delete(dir, recursive: true);
    }

    // --- ECS ---

    [Test]
    public async Task ECSタスクはSSMターゲット形式に変換される()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("ListTasks", AwsResponses.EcsTaskArns("arn:aws:ecs:ap-northeast-1:123:task/prod-cluster/abc123"));
        aws.On("DescribeTasks", AwsResponses.EcsTasks(
            [("arn:aws:ecs:ap-northeast-1:123:task/prod-cluster/abc123", "app", "runtime-1", "10.0.3.21")]));

        var outcome = await ResolveAsync(catalog, new EcsTaskQuery(
            ClusterName.Create("prod-cluster").Value,
            ServiceName.Create("api").Value,
            null,
            MatchPolicy.RequireSingle));

        var resolved = (ResolutionOutcome.Resolved)outcome;
        await Assert.That(resolved.Resource.SsmTarget!.Value).IsEqualTo("ecs:prod-cluster_abc123_runtime-1");
        await Assert.That(resolved.Resource.Host!.Value).IsEqualTo("10.0.3.21");

        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task 踏み台のECSタスクにRuntimeIdが無ければExec無効として失敗する()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("ListTasks", AwsResponses.EcsTaskArns("arn:aws:ecs:ap-northeast-1:123:task/prod-cluster/abc123"));
        aws.On("DescribeTasks", AwsResponses.EcsTasks(
            [("arn:aws:ecs:ap-northeast-1:123:task/prod-cluster/abc123", "app", null, "10.0.3.21")]));

        var outcome = await ResolveAsync(
            catalog,
            new EcsTaskQuery(ClusterName.Create("prod-cluster").Value, null, null, MatchPolicy.RequireSingle),
            FailurePhase.ResolveGateway);

        var failed = (ResolutionOutcome.Failed)outcome;
        await Assert.That(failed.Error.Code).IsEqualTo("EcsExecUnavailable");
        await Assert.That(failed.Error.Phase).IsEqualTo(FailurePhase.ResolveGateway);

        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public async Task 実行中タスクが無ければNotFound()
    {
        var (aws, catalog, dir) = await CreateAsync();
        await using var _ = aws;
        aws.On("ListTasks", AwsResponses.EcsTaskArns());

        var outcome = await ResolveAsync(catalog, new EcsTaskQuery(
            ClusterName.Create("prod-cluster").Value, null, null, MatchPolicy.RequireSingle));

        await Assert.That(outcome).IsTypeOf<ResolutionOutcome.NotFound>();
        await Assert.That(aws.Received("DescribeTasks")).IsFalse();

        Directory.Delete(dir, recursive: true);
    }
}
