using Amazon;
using Amazon.EC2;
using Amazon.ECS;
using Amazon.ElastiCache;
using Amazon.RDS;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SimpleSystemsManagement;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Aws;

/// <summary>名前付きプロファイルから資格情報を解決し、リージョン付きの AWS クライアントを作る。</summary>
/// <param name="endpointOverride">
/// 全サービスのエンドポイントを差し替える。VPC エンドポイント指定と、
/// 結合テストでローカルのスタブサーバへ向けるために使う。null なら通常のリージョン解決。
/// </param>
/// <param name="profilesLocation">資格情報ファイルのパス。null なら既定の探索順。</param>
public sealed class AwsClientFactory(string? endpointOverride = null, string? profilesLocation = null)
{
    private readonly CredentialProfileStoreChain _chain = profilesLocation is { Length: > 0 } path
        ? new CredentialProfileStoreChain(path)
        : new CredentialProfileStoreChain();

    /// <summary>SSO (IAM Identity Center) プロファイルかどうか。エラー文言の出し分けに使う。</summary>
    public bool IsSsoProfile(ProfileName profile) =>
        _chain.TryGetProfile(profile.Value, out var p) &&
        (!string.IsNullOrEmpty(p.Options.SsoStartUrl) || !string.IsNullOrEmpty(p.Options.SsoSession));

    public Result<AWSCredentials, ErrorDetail> GetCredentials(ProfileName profile)
    {
        if (!_chain.TryGetAWSCredentials(profile.Value, out var credentials))
        {
            return Result<AWSCredentials, ErrorDetail>.Failure(new ErrorDetail(
                FailurePhase.Credentials,
                "ProfileNotFound",
                $"プロファイル「{profile.Value}」が見つかりません。~/.aws/config を確認してください。"));
        }

        return Result<AWSCredentials, ErrorDetail>.Success(credentials);
    }

    private Result<TClient, ErrorDetail> Create<TConfig, TClient>(
        AwsContext aws, TConfig config, Func<AWSCredentials, TConfig, TClient> create)
        where TConfig : ClientConfig
    {
        return GetCredentials(aws.Profile).Map(credentials =>
        {
            if (endpointOverride is { Length: > 0 } url)
            {
                config.ServiceURL = url;
                config.AuthenticationRegion = aws.Region.Value;
            }
            else
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(aws.Region.Value);
            }

            return create(credentials, config);
        });
    }

    public Result<IAmazonSimpleSystemsManagement, ErrorDetail> CreateSsm(AwsContext aws) =>
        Create(aws, new AmazonSimpleSystemsManagementConfig(),
            (c, cfg) => (IAmazonSimpleSystemsManagement)new AmazonSimpleSystemsManagementClient(c, cfg));

    public Result<IAmazonEC2, ErrorDetail> CreateEc2(AwsContext aws) =>
        Create(aws, new AmazonEC2Config(), (c, cfg) => (IAmazonEC2)new AmazonEC2Client(c, cfg));

    public Result<IAmazonECS, ErrorDetail> CreateEcs(AwsContext aws) =>
        Create(aws, new AmazonECSConfig(), (c, cfg) => (IAmazonECS)new AmazonECSClient(c, cfg));

    public Result<IAmazonRDS, ErrorDetail> CreateRds(AwsContext aws) =>
        Create(aws, new AmazonRDSConfig(), (c, cfg) => (IAmazonRDS)new AmazonRDSClient(c, cfg));

    public Result<IAmazonElastiCache, ErrorDetail> CreateElastiCache(AwsContext aws) =>
        Create(aws, new AmazonElastiCacheConfig(),
            (c, cfg) => (IAmazonElastiCache)new AmazonElastiCacheClient(c, cfg));
}
