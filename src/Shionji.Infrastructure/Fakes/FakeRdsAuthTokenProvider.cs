using Shionji.Domain.Ports;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Fakes;

/// <summary>デモモードでは実際の AWS 資格情報に触れない。</summary>
public sealed class FakeRdsAuthTokenProvider : IRdsAuthTokenProvider
{
    public Task<Result<string, ErrorDetail>> GenerateAsync(
        AwsContext aws, HostName host, Port port, DatabaseUserName userName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<string, ErrorDetail>.Failure(new ErrorDetail(
            FailurePhase.DatabaseAuthentication, "DemoMode",
            "デモモードでは RDS IAM 認証トークンを生成できません。")));
}
