using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Configuration;

/// <summary>クエリが複数リソースに一致した場合の扱い。</summary>
public enum MatchPolicy
{
    /// <summary>一意に定まることを要求する。複数一致は Ambiguous として扱う (既定)。</summary>
    RequireSingle,

    /// <summary>リソース型ごとの既定順序で先頭の 1 件を採用する。</summary>
    PickFirst,
}

public enum CacheEndpointRole
{
    Primary,
    Reader,
    Configuration,
}

public enum AuroraEndpointRole
{
    Writer,
    Reader,
}

/// <summary>AWS リソースを自動特定する検索条件。</summary>
public abstract record ResourceQuery(NamePattern? Name, TagFilters Tags, MatchPolicy Match)
{
    /// <summary>名前パターンによる客側フィルタ。パターン未指定ならすべて一致。</summary>
    public bool MatchesName(string candidateName) => Name is null || Name.IsMatch(candidateName);

    public bool MatchesTags(IReadOnlyDictionary<string, string> tags) => Tags.IsSatisfiedBy(tags);
}

/// <summary>ElastiCache (レプリケーショングループ / クラスター) の検索。</summary>
public sealed record ElastiCacheQuery(
    NamePattern? Name,
    TagFilters Tags,
    MatchPolicy Match,
    CacheEndpointRole Role) : ResourceQuery(Name, Tags, Match);

/// <summary>Aurora DB クラスターの検索。</summary>
public sealed record AuroraQuery(
    NamePattern? Name,
    TagFilters Tags,
    MatchPolicy Match,
    AuroraEndpointRole Role) : ResourceQuery(Name, Tags, Match);

/// <summary>EC2 インスタンスの検索。running のインスタンスのみ対象。名前は Name タグに一致させる。</summary>
public sealed record Ec2Query(
    NamePattern? Name,
    TagFilters Tags,
    MatchPolicy Match) : ResourceQuery(Name, Tags, Match);

/// <summary>ECS タスクの検索。クラスター (+ サービス) 内の実行中タスクを対象とする。</summary>
public sealed record EcsTaskQuery(
    ClusterName Cluster,
    ServiceName? Service,
    ContainerName? Container,
    MatchPolicy Match) : ResourceQuery(null, TagFilters.Empty, Match);
