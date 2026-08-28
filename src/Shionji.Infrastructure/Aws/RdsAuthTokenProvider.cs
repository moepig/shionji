using Amazon;
using Amazon.RDS.Util;
using Shionji.Domain.Ports;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Aws;

/// <summary>接続先設定のプロファイルとリージョンを使い、AWS SDK で署名する。</summary>
public sealed class RdsAuthTokenProvider(AwsClientFactory clients) : IRdsAuthTokenProvider
{
    public async Task<Result<string, ErrorDetail>> GenerateAsync(
        AwsContext aws, HostName host, Port port, DatabaseUserName userName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var credentials = clients.GetCredentials(aws);
            if (credentials.IsFailure)
                return Result<string, ErrorDetail>.Failure(credentials.Error);

            // SDK が SSO / AssumeRole の資格情報を必要に応じて更新する。既定プロファイルには依存しない。
            // AWS API の endpointOverride も、DB に対する署名には使わない。
            var token = await RDSAuthTokenGenerator.GenerateAuthTokenAsync(
                credentials.Value, RegionEndpoint.GetBySystemName(aws.Region.Value),
                host.Value, port.Value, userName.Value).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Result<string, ErrorDetail>.Success(token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = AwsErrors.Classify(
                ex, FailurePhase.DatabaseAuthentication, aws.Profile, clients.IsSsoProfile(aws.Profile));
            // SDK 例外の本文には資格情報が含まれうるため、そのまま UI やログへ渡さない。
            return Result<string, ErrorDetail>.Failure(error with
            {
                Message = error.Phase == FailurePhase.Credentials
                    ? error.Message
                    : "RDS IAM 認証トークンを生成できません。AWS プロファイル、リージョン、DB エンドポイントを確認してください。",
            });
        }
    }
}
