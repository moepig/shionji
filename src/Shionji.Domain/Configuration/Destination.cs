using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Configuration;

/// <summary>ローカルポートの先に届けたい相手。</summary>
public abstract record Destination
{
    private Destination() { }

    /// <summary>エンドポイント / IP の直接指定。</summary>
    public sealed record Static(HostName Host, Port Port) : Destination;

    /// <summary>リソースクエリによる自動特定。</summary>
    public sealed record Query(ResourceQuery ResourceQuery, PortSelection Port) : Destination;
}

/// <summary>クエリで特定した転送先の接続ポートの決め方。</summary>
public abstract record PortSelection
{
    private PortSelection() { }

    /// <summary>ポートを明示指定する。</summary>
    public sealed record Explicit(Port Port) : PortSelection;

    /// <summary>リソースが持つ既定ポートを使う (ElastiCache / Aurora のみ)。</summary>
    public sealed record FromResource : PortSelection
    {
        public static readonly FromResource Instance = new();
    }
}
