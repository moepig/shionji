using Shionji.Domain.Configuration;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Storage;

namespace Shionji.Infrastructure.Tests;

public class JsonRepositoryTests
{
    private static ForwardingConfig Config(string name, ConfigId? id = null) =>
        ForwardingConfig.Create(
            id ?? ConfigId.New(),
            ConfigName.Create(name).Value,
            new AwsContext(ProfileName.Create("dev").Value, AwsRegion.Create("ap-northeast-1").Value),
            new LocalPortSpec.Fixed(Port.Create(15432).Value),
            new Destination.Static(HostName.Create("db.example.internal").Value, Port.Create(5432).Value),
            new GatewaySpec.Ec2(new Ec2Selector.ById(InstanceId.Create("i-0123456789abcdef0").Value)),
            ConfigOptions.Default).Value;

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"shionji-test-{Guid.NewGuid():N}", "configs.json");

    [Test]
    public async Task 保存した設定をロードできる()
    {
        var path = TempFile();
        try
        {
            var repository = new JsonForwardingConfigRepository(path);
            var a = Config("a");
            var b = Config("b");

            await repository.SaveAsync(a);
            await repository.SaveAsync(b);

            var loaded = await new JsonForwardingConfigRepository(path).LoadAllAsync();
            await Assert.That(loaded.Count).IsEqualTo(2);
            await Assert.That(loaded.Select(c => c.Name.Value).Order()).IsEquivalentTo(["a", "b"]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Test]
    public async Task 上書き保存と削除()
    {
        var path = TempFile();
        try
        {
            var repository = new JsonForwardingConfigRepository(path);
            var config = Config("original");
            await repository.SaveAsync(config);

            var updated = Config("renamed", config.Id);
            await repository.SaveAsync(updated);

            var afterUpdate = await repository.LoadAllAsync();
            await Assert.That(afterUpdate.Count).IsEqualTo(1);
            await Assert.That(afterUpdate[0].Name.Value).IsEqualTo("renamed");

            await repository.DeleteAsync(config.Id);
            var afterDelete = await repository.LoadAllAsync();
            await Assert.That(afterDelete.Count).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Test]
    public async Task ファイルが無ければ空を返す()
    {
        var repository = new JsonForwardingConfigRepository(TempFile());
        var loaded = await repository.LoadAllAsync();
        await Assert.That(loaded.Count).IsEqualTo(0);
    }
}
