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

    [Test]
    public async Task 壊れたファイルは退避され以後の保存も動く()
    {
        // 例外をそのまま投げると一覧が黙って空になり、保存も一切できなくなる
        using var dir = new TempDir();
        var path = dir.File("configs.json");
        await File.WriteAllTextAsync(path, "{ truncated...");
        var repository = new JsonForwardingConfigRepository(path);

        var loaded = await repository.LoadAllAsync();
        await Assert.That(loaded.Count).IsEqualTo(0);

        // 破損ファイルは復旧できるよう退避されている
        var quarantined = Directory.GetFiles(dir.Path, "configs.json.corrupt-*");
        await Assert.That(quarantined.Length).IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(quarantined[0])).IsEqualTo("{ truncated...");

        // 破損後も新しい設定を保存して読み戻せる
        var config = Config("recovered");
        await repository.SaveAsync(config);
        var reloaded = await repository.LoadAllAsync();
        await Assert.That(reloaded.Single().Name.Value).IsEqualTo("recovered");
    }

    [Test]
    public async Task 変換できないエントリだけを飛ばして残りを読む()
    {
        using var dir = new TempDir();
        var path = dir.File("configs.json");
        var repository = new JsonForwardingConfigRepository(path);
        await repository.SaveAsync(Config("valid"));

        // 手編集でポート番号が範囲外になった 1 件を混ぜる
        var document = await File.ReadAllTextAsync(path);
        var broken = document.Replace("\"Configs\": [", """
            "Configs": [
              {
                "Id": "11111111-1111-1111-1111-111111111111",
                "Name": "broken",
                "Profile": "dev",
                "Region": "ap-northeast-1",
                "LocalPort": 99999,
                "Destination": { "kind": "static", "Host": "h.example.com", "Port": 5432 },
                "Gateway": { "kind": "ec2", "InstanceId": "i-0123456789abcdef0" }
              },
            """);
        await File.WriteAllTextAsync(path, broken);

        var loaded = await repository.LoadAllAsync();

        await Assert.That(loaded.Single().Name.Value).IsEqualTo("valid");
    }

    [Test]
    public async Task 保存の途中経過ファイルを残さない()
    {
        using var dir = new TempDir();
        var path = dir.File("configs.json");
        var repository = new JsonForwardingConfigRepository(path);

        await repository.SaveAsync(Config("a"));

        await Assert.That(File.Exists(path + ".tmp")).IsFalse();
    }

    [Test]
    public async Task 同じ設定を並行して保存しても壊れない()
    {
        using var dir = new TempDir();
        var path = dir.File("configs.json");
        var repository = new JsonForwardingConfigRepository(path);
        var configs = Enumerable.Range(0, 20).Select(i => Config($"config-{i:00}")).ToList();

        await Task.WhenAll(configs.Select(c => repository.SaveAsync(c)));

        var loaded = await repository.LoadAllAsync();
        await Assert.That(loaded.Count).IsEqualTo(20);
    }
}
