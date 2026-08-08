using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Aws;

namespace Shionji.Infrastructure.Tests;

public class CandidateSelectionTests
{
    private static ResolvedResource Resource(string name) =>
        new(new ResourceId($"id-{name}"), name, null, null, null, DateTimeOffset.UnixEpoch);

    [Test]
    public async Task 候補ゼロはNotFound()
    {
        var outcome = CandidateSelection.Apply(MatchPolicy.RequireSingle, []);
        await Assert.That(outcome).IsTypeOf<ResolutionOutcome.NotFound>();
    }

    [Test]
    public async Task 候補1件はポリシーによらずResolved()
    {
        var single = new[] { Resource("only") };
        await Assert.That(CandidateSelection.Apply(MatchPolicy.RequireSingle, single))
            .IsTypeOf<ResolutionOutcome.Resolved>();
        await Assert.That(CandidateSelection.Apply(MatchPolicy.PickFirst, single))
            .IsTypeOf<ResolutionOutcome.Resolved>();
    }

    [Test]
    public async Task RequireSingleで複数一致はAmbiguousになり名前順に並ぶ()
    {
        var outcome = CandidateSelection.Apply(
            MatchPolicy.RequireSingle, [Resource("beta"), Resource("Alpha")]);

        var ambiguous = (ResolutionOutcome.Ambiguous)outcome;
        await Assert.That(ambiguous.Candidates.Select(c => c.DisplayName))
            .IsEquivalentTo(["Alpha", "beta"]);
    }

    [Test]
    public async Task PickFirstは既定順序の先頭を採用する()
    {
        var outcome = CandidateSelection.Apply(
            MatchPolicy.PickFirst, [Resource("beta"), Resource("Alpha")]);

        var resolved = (ResolutionOutcome.Resolved)outcome;
        await Assert.That(resolved.Resource.DisplayName).IsEqualTo("Alpha");
    }
}
