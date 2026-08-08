using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tests;

public class NamePatternTests
{
    [Test]
    [Arguments("prod-db", "prod-db", true)]
    [Arguments("prod-db", "prod-db-2", false)]
    [Arguments("prod-*", "prod-db-01", true)]
    [Arguments("prod-*", "staging-db", false)]
    [Arguments("*-cache", "session-cache", true)]
    [Arguments("*-cache", "cache-v2", false)]
    [Arguments("db-?", "db-1", true)]
    [Arguments("db-?", "db-12", false)]
    [Arguments("*", "anything", true)]
    public async Task glob照合(string pattern, string candidate, bool expected)
    {
        var result = TestData.Pattern(pattern).IsMatch(candidate);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task 大文字小文字は区別しない()
    {
        await Assert.That(TestData.Pattern("Prod-*").IsMatch("prod-DB")).IsTrue();
    }

    [Test]
    public async Task 正規表現メタ文字はリテラルとして扱う()
    {
        await Assert.That(TestData.Pattern("a.b").IsMatch("aXb")).IsFalse();
        await Assert.That(TestData.Pattern("a.b").IsMatch("a.b")).IsTrue();
        await Assert.That(TestData.Pattern("a+b").IsMatch("a+b")).IsTrue();
    }

    [Test]
    public async Task 空のパターンは失敗する()
    {
        await Assert.That(NamePattern.Create("  ").IsFailure).IsTrue();
    }

    [Test]
    public async Task 等価性は文字列で判定する()
    {
        await Assert.That(TestData.Pattern("a*")).IsEqualTo(TestData.Pattern("a*"));
    }
}
