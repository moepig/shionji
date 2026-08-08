using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Fakes;

namespace Shionji.Infrastructure.Tests;

/// <summary>
/// デモモードの体験は本物と同じ経路で確かめられている必要がある。
/// とくに、ハンドルが返る前に出たログが失われないこと。
/// </summary>
public class FakeTunnelLauncherTests
{
    [Test]
    public async Task 起動直後のログは購読が後からでも届く()
    {
        var launched = await new FakeTunnelLauncher().LaunchAsync(Plan());

        await Assert.That(launched.IsSuccess).IsTrue();
        await using var handle = launched.Value;

        // LaunchAsync が返ってから購読する (TunnelSupervisor と同じ順序)
        List<string> lines = [];
        handle.LogEmitted += (_, e) => lines.Add(e.Line);

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0]).Contains("Port 15432 opened");
    }

    [Test]
    public async Task 溜めた分を配るのは最初の購読者だけ()
    {
        var launched = await new FakeTunnelLauncher().LaunchAsync(Plan());
        await using var handle = launched.Value;

        List<string> first = [];
        handle.LogEmitted += (_, e) => first.Add(e.Line);
        List<string> second = [];
        handle.LogEmitted += (_, e) => second.Add(e.Line);

        await Assert.That(first.Count).IsEqualTo(1);
        await Assert.That(second).IsEmpty();
    }

    private static TunnelPlan Plan() => new(
        new AwsContext(ProfileName.Create("demo").Value, AwsRegion.Create("ap-northeast-1").Value),
        new SsmTargetId("i-0123456789abcdef0"),
        new SessionMode.RemoteHostForward(
            HostName.Create("db.example.internal").Value, Port.Create(5432).Value),
        Port.Create(15432).Value);
}
