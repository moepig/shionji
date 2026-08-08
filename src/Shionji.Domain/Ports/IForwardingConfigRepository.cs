using Shionji.Domain.Configuration;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Ports;

/// <summary>転送設定の永続化。</summary>
public interface IForwardingConfigRepository
{
    Task<IReadOnlyList<ForwardingConfig>> LoadAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ForwardingConfig config, CancellationToken cancellationToken = default);

    Task DeleteAsync(ConfigId id, CancellationToken cancellationToken = default);
}
