using Shionji.Domain.Primitives;

namespace Shionji.Domain.Tests;

public class ResultTests
{
    [Test]
    public async Task 成功はValueを返しErrorは例外になる()
    {
        var result = Result<int, string>.Success(42);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42);
        await Assert.That(() => _ = result.Error).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task 失敗はErrorを返しValueは例外になる()
    {
        var result = Result<int, string>.Failure("ng");
        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error).IsEqualTo("ng");
        await Assert.That(() => _ = result.Value).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MapとBindは成功時のみ適用される()
    {
        var success = Result<int, string>.Success(2).Map(x => x * 10);
        var failure = Result<int, string>.Failure("ng").Map(x => x * 10);
        await Assert.That(success.Value).IsEqualTo(20);
        await Assert.That(failure.Error).IsEqualTo("ng");

        var bound = Result<int, string>.Success(2)
            .Bind(x => Result<string, string>.Success($"v{x}"));
        await Assert.That(bound.Value).IsEqualTo("v2");
    }

    [Test]
    public async Task Matchは対応する側の関数を呼ぶ()
    {
        var success = Result<int, string>.Success(1).Match(v => $"ok:{v}", e => $"ng:{e}");
        var failure = Result<int, string>.Failure("x").Match(v => $"ok:{v}", e => $"ng:{e}");
        await Assert.That(success).IsEqualTo("ok:1");
        await Assert.That(failure).IsEqualTo("ng:x");
    }
}
