using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tests;

public class TagFiltersTests
{
    private static TagFilter Filter(string key, params string[] values) =>
        TagFilter.Create(key, values).Value;

    private static readonly IReadOnlyDictionary<string, string> Tags = new Dictionary<string, string>
    {
        ["Environment"] = "production",
        ["Team"] = "platform",
    };

    [Test]
    public async Task 空のフィルタはすべてに一致する()
    {
        await Assert.That(TagFilters.Empty.IsSatisfiedBy(Tags)).IsTrue();
    }

    [Test]
    public async Task 単一条件の一致()
    {
        var filters = TagFilters.Of(Filter("Environment", "production"));
        await Assert.That(filters.IsSatisfiedBy(Tags)).IsTrue();
    }

    [Test]
    public async Task 値はいずれかに一致すればよい()
    {
        var filters = TagFilters.Of(Filter("Environment", "staging", "production"));
        await Assert.That(filters.IsSatisfiedBy(Tags)).IsTrue();
    }

    [Test]
    public async Task 複数条件はすべて満たす必要がある()
    {
        var both = TagFilters.Of(Filter("Environment", "production"), Filter("Team", "platform"));
        var mismatch = TagFilters.Of(Filter("Environment", "production"), Filter("Team", "web"));
        await Assert.That(both.IsSatisfiedBy(Tags)).IsTrue();
        await Assert.That(mismatch.IsSatisfiedBy(Tags)).IsFalse();
    }

    [Test]
    public async Task キーが存在しなければ不一致()
    {
        var filters = TagFilters.Of(Filter("Missing", "x"));
        await Assert.That(filters.IsSatisfiedBy(Tags)).IsFalse();
    }

    [Test]
    public async Task タグ値は大文字小文字を区別する()
    {
        var filters = TagFilters.Of(Filter("Environment", "Production"));
        await Assert.That(filters.IsSatisfiedBy(Tags)).IsFalse();
    }

    [Test]
    public async Task 空のキーや値は作成できない()
    {
        await Assert.That(TagFilter.Create("", ["x"]).IsFailure).IsTrue();
        await Assert.That(TagFilter.Create("Key", []).IsFailure).IsTrue();
        await Assert.That(TagFilter.Create("Key", ["", "  "]).IsFailure).IsTrue();
    }
}
