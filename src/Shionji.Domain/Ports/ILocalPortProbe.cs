using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Ports;

/// <summary>ローカルポートの空き確認と自動割当。</summary>
public interface ILocalPortProbe
{
    bool IsAvailable(Port port);

    /// <summary>OS に空きポートを割り当てさせる。</summary>
    Result<Port, ErrorDetail> AcquireFreePort();
}
