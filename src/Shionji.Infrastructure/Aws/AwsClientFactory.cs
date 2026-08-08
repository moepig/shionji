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
public sealed class AwsClientFactory
{
    private readonly CredentialProfileStoreChain _chain = new();

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

    public Result<T, ErrorDetail> Create<T>(AwsContext aws, Func<AWSCredentials, RegionEndpoint, T> create) =>
        GetCredentials(aws.Profile)
            .Map(credentials => create(credentials, RegionEndpoint.GetBySystemName(aws.Region.Value)));

    public Result<IAmazonSimpleSystemsManagement, ErrorDetail> CreateSsm(AwsContext aws) =>
        Create<IAmazonSimpleSystemsManagement>(aws, (c, r) => new AmazonSimpleSystemsManagementClient(c, r));

    public Result<IAmazonEC2, ErrorDetail> CreateEc2(AwsContext aws) =>
        Create<IAmazonEC2>(aws, (c, r) => new AmazonEC2Client(c, r));

    public Result<IAmazonECS, ErrorDetail> CreateEcs(AwsContext aws) =>
        Create<IAmazonECS>(aws, (c, r) => new AmazonECSClient(c, r));

    public Result<IAmazonRDS, ErrorDetail> CreateRds(AwsContext aws) =>
        Create<IAmazonRDS>(aws, (c, r) => new AmazonRDSClient(c, r));

    public Result<IAmazonElastiCache, ErrorDetail> CreateElastiCache(AwsContext aws) =>
        Create<IAmazonElastiCache>(aws, (c, r) => new AmazonElastiCacheClient(c, r));
}
