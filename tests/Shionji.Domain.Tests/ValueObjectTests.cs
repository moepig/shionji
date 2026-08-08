using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tests;

public class PortTests
{
    [Test]
    [Arguments(1)]
    [Arguments(5432)]
    [Arguments(65535)]
    public async Task 有効な範囲のポートを作成できる(int value)
    {
        var result = Port.Create(value);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value.Value).IsEqualTo(value);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(65536)]
    public async Task 範囲外のポートは失敗する(int value)
    {
        var result = Port.Create(value);
        await Assert.That(result.IsFailure).IsTrue();
    }
}

public class HostNameTests
{
    [Test]
    [Arguments("db.example.internal")]
    [Arguments("10.0.12.34")]
    [Arguments("  trimmed.example.com  ")]
    public async Task 有効なホスト名を作成できる(string value)
    {
        var result = HostName.Create(value);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value.Value).IsEqualTo(value.Trim());
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("has space.example.com")]
    public async Task 不正なホスト名は失敗する(string value)
    {
        var result = HostName.Create(value);
        await Assert.That(result.IsFailure).IsTrue();
    }
}

public class ConfigNameTests
{
    [Test]
    public async Task 空の設定名は失敗する()
    {
        await Assert.That(ConfigName.Create("").IsFailure).IsTrue();
        await Assert.That(ConfigName.Create(new string('a', 65)).IsFailure).IsTrue();
        await Assert.That(ConfigName.Create("api-db").IsSuccess).IsTrue();
    }
}

public class AwsRegionTests
{
    [Test]
    [Arguments("ap-northeast-1")]
    [Arguments("us-east-1")]
    public async Task 有効なリージョンを作成できる(string value)
    {
        await Assert.That(AwsRegion.Create(value).IsSuccess).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("AP-NORTHEAST-1")]
    [Arguments("ap northeast")]
    public async Task 不正なリージョンは失敗する(string value)
    {
        await Assert.That(AwsRegion.Create(value).IsFailure).IsTrue();
    }
}

public class InstanceIdTests
{
    [Test]
    [Arguments("i-0123456789abcdef0")]
    [Arguments("i-12345678")]
    public async Task 有効なインスタンスIDを作成できる(string value)
    {
        await Assert.That(InstanceId.Create(value).IsSuccess).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("i-XYZ")]
    [Arguments("instance-1")]
    public async Task 不正なインスタンスIDは失敗する(string value)
    {
        await Assert.That(InstanceId.Create(value).IsFailure).IsTrue();
    }
}

public class SsmTargetIdTests
{
    [Test]
    public async Task EC2インスタンスのターゲットはインスタンスIDそのもの()
    {
        var target = SsmTargetId.ForInstance(TestData.Instance("i-0123456789abcdef0"));
        await Assert.That(target.Value).IsEqualTo("i-0123456789abcdef0");
    }

    [Test]
    public async Task ECSタスクのターゲットは規定の形式になる()
    {
        var target = SsmTargetId.ForEcsTask(TestData.Cluster("prod"), "task123", "runtime456");
        await Assert.That(target.Value).IsEqualTo("ecs:prod_task123_runtime456");
    }
}
