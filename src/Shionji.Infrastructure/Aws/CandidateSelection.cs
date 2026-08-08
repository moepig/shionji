using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;

namespace Shionji.Infrastructure.Aws;

/// <summary>一致候補の集合を MatchPolicy に従って解決結果へ確定する (純粋関数)。</summary>
public static class CandidateSelection
{
    public static ResolutionOutcome Apply(MatchPolicy policy, IReadOnlyList<ResolvedResource> candidates)
    {
        if (candidates.Count == 0)
            return ResolutionOutcome.NotFound.Instance;

        var ordered = candidates
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count == 1 || policy == MatchPolicy.PickFirst)
            return new ResolutionOutcome.Resolved(ordered[0]);

        return new ResolutionOutcome.Ambiguous(ordered);
    }
}
