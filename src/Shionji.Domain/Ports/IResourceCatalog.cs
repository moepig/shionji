using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Ports;

/// <summary>リソースクエリを実リソースへ解決する。</summary>
public interface IResourceCatalog
{
    /// <param name="phase">この解決が失敗した場合に使うフェーズ (転送先 / 踏み台)。</param>
    Task<ResolutionOutcome> ResolveAsync(
        AwsContext aws,
        ResourceQuery query,
        FailurePhase phase,
        CancellationToken cancellationToken = default);
}
