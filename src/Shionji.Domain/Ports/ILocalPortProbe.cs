using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Ports;

/// <summary>ローカルポートの空き確認と自動割当。</summary>
public interface ILocalPortProbe
{
    bool IsAvailable(Port port);

    /// <summary>
    /// 誰かがそのポートを listen しているか。トンネル確立の判定に使う
    /// (plugin の出力文言に依存せず、ポートが実際に開いたことを直接確かめる)。
    /// </summary>
    bool IsListening(Port port);

    /// <summary>OS に空きポートを割り当てさせる。</summary>
    Result<Port, ErrorDetail> AcquireFreePort();
}
