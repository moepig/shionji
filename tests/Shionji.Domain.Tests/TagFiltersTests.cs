using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tests;

public class TagFiltersTests
{
    private static TagFilter Filter(string key, string value) =>
        TagFilter.Create(key, value).Value;

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
    public async Task 値が違えば不一致()
    {
        var filters = TagFilters.Of(Filter("Environment", "staging"));
        await Assert.That(filters.IsSatisfiedBy(Tags)).IsFalse();
    }

    [Test]
    public async Task 複数条件はすべて満たす必要がある()
    {
        // 並べた条件は AND。いずれかを満たせばよい (OR) ではない
        var both = TagFilters.Of(Filter("Environment", "production"), Filter("Team", "platform"));
        var mismatch = TagFilters.Of(Filter("Environment", "production"), Filter("Team", "web"));
        await Assert.That(both.IsSatisfiedBy(Tags)).IsTrue();
        await Assert.That(mismatch.IsSatisfiedBy(Tags)).IsFalse();
    }

    [Test]
    public async Task 同じキーに違う値を並べるとどのリソースにも一致しない()
    {
        // AND なので、同じキーに 2 つの値を与えると矛盾する
        var filters = TagFilters.Of(Filter("Environment", "production"), Filter("Environment", "staging"));
        await Assert.That(filters.IsSatisfiedBy(Tags)).IsFalse();
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
        await Assert.That(TagFilter.Create("", "x").IsFailure).IsTrue();
        await Assert.That(TagFilter.Create("Key", "").IsFailure).IsTrue();
        await Assert.That(TagFilter.Create("Key", "   ").IsFailure).IsTrue();
    }

    [Test]
    public async Task 前後の空白は落とす()
    {
        var filter = Filter("  Environment  ", "  production  ");
        await Assert.That(filter.Key).IsEqualTo("Environment");
        await Assert.That(filter.Value).IsEqualTo("production");
    }
}
