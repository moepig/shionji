using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Ports;

/// <summary>DB 接続に使う RDS IAM 認証トークンを都度生成する。トークンは永続化しない。</summary>
public interface IRdsAuthTokenProvider
{
    /// <summary>ローカルの転送先ではなく、実際の DB エンドポイントと DB ポートで署名する。</summary>
    Task<Result<string, ErrorDetail>> GenerateAsync(
        AwsContext aws, HostName host, Port port, DatabaseUserName userName,
        CancellationToken cancellationToken = default);
}
