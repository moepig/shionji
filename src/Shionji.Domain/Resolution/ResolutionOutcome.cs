using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Resolution;

/// <summary>クエリから確定した実リソース。</summary>
/// <param name="Id">リソースの識別子 (ARN やインスタンス ID など)。</param>
/// <param name="DisplayName">一覧表示用の名前。</param>
/// <param name="Host">接続に使うエンドポイント / プライベート IP。SSM ターゲット専用リソースでは null。</param>
/// <param name="DefaultPort">リソースが持つ既定ポート (ElastiCache / Aurora)。無ければ null。</param>
/// <param name="SsmTarget">SSM セッションを張れるリソースの場合のターゲット ID。</param>
/// <param name="ResolvedAt">解決した時刻。</param>
public sealed record ResolvedResource(
    ResourceId Id,
    string DisplayName,
    HostName? Host,
    Port? DefaultPort,
    SsmTargetId? SsmTarget,
    DateTimeOffset ResolvedAt);

/// <summary>リソースクエリの解決結果。</summary>
public abstract record ResolutionOutcome
{
    private ResolutionOutcome() { }

    /// <summary>一意に解決できた。</summary>
    public sealed record Resolved(ResolvedResource Resource) : ResolutionOutcome;

    /// <summary>条件に一致するリソースが存在しない。</summary>
    public sealed record NotFound : ResolutionOutcome
    {
        public static readonly NotFound Instance = new();
    }

    /// <summary>複数のリソースが一致した (MatchPolicy.RequireSingle)。</summary>
    public sealed record Ambiguous(IReadOnlyList<ResolvedResource> Candidates) : ResolutionOutcome;

    /// <summary>解決処理そのものが失敗した。</summary>
    public sealed record Failed(ErrorDetail Error) : ResolutionOutcome;
}
