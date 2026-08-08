using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Ports;

/// <summary>リソースクエリを実リソースへ解決する。</summary>
public interface IResourceCatalog
{
    Task<ResolutionOutcome> ResolveAsync(
        AwsContext aws,
        ResourceQuery query,
        CancellationToken cancellationToken = default);
}
