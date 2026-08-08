using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Application;

/// <summary>経路指定から解決が必要なリソースクエリを導出する。</summary>
internal static class GatewayQueries
{
    /// <returns>解決が不要な経路 (Direct / インスタンス ID 直接指定) の場合は null。</returns>
    public static ResourceQuery? QueryFor(GatewaySpec gateway) => gateway switch
    {
        GatewaySpec.Ec2 { Selector: Ec2Selector.ByQuery byQuery } => byQuery.Query,
        GatewaySpec.Ecs ecs => new EcsTaskQuery(ecs.Cluster, ecs.Service, ecs.Container, MatchPolicy.RequireSingle),
        _ => null,
    };
}

internal static class SafeResolver
{
    /// <summary>カタログの予期しない例外を Failed 化して解決結果に閉じ込める。</summary>
    public static async Task<ResolutionOutcome> ResolveAsync(
        IResourceCatalog catalog,
        AwsContext aws,
        ResourceQuery query,
        FailurePhase phase,
        CancellationToken cancellationToken)
    {
        try
        {
            return await catalog.ResolveAsync(aws, query, phase, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ResolutionOutcome.Failed(new ErrorDetail(phase, "Unexpected", ex.Message));
        }
    }
}

internal static class OutcomeErrors
{
    /// <summary>接続を進められない解決結果をエラーへ変換する。Resolved は null。</summary>
    public static ErrorDetail? ToErrorDetail(ResolutionOutcome outcome, FailurePhase phase) => outcome switch
    {
        ResolutionOutcome.Resolved => null,
        ResolutionOutcome.NotFound => new ErrorDetail(phase, "NotFound", "条件に一致するリソースが見つかりません。"),
        ResolutionOutcome.Ambiguous a => new ErrorDetail(
            phase, "Ambiguous", $"条件に一致するリソースが {a.Candidates.Count} 件あります。条件を絞り込んでください。"),
        ResolutionOutcome.Failed f => f.Error,
        _ => throw new InvalidOperationException($"未知の解決結果型: {outcome.GetType()}"),
    };
}
