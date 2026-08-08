using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Configuration;

/// <summary>ローカル側で待ち受けるポートの指定。</summary>
public abstract record LocalPortSpec
{
    private LocalPortSpec() { }

    /// <summary>固定ポートで待ち受ける。</summary>
    public sealed record Fixed(Port Port) : LocalPortSpec;

    /// <summary>接続時に OS が空きポートを割り当てる。</summary>
    public sealed record Auto : LocalPortSpec
    {
        public static readonly Auto Instance = new();
    }
}
