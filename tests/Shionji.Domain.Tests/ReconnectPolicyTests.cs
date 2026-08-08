using Shionji.Domain.Tunneling;

namespace Shionji.Domain.Tests;

public class ReconnectPolicyTests
{
    [Test]
    [Arguments(1, 2)]
    [Arguments(2, 4)]
    [Arguments(3, 8)]
    [Arguments(4, 16)]
    [Arguments(5, 30)]
    public async Task 既定方針は2秒から倍々で30秒が上限(int attempt, int expectedSeconds)
    {
        var delay = ReconnectPolicy.Default.DelayFor(attempt);
        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Test]
    public async Task 試行回数は1以上を要求する()
    {
        await Assert.That(() => ReconnectPolicy.Default.DelayFor(0))
            .Throws<ArgumentOutOfRangeException>();
    }
}
