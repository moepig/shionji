using Shionji.Infrastructure.Storage;

namespace Shionji.Infrastructure.Tests;

public class StorageLocationsTests
{
    [Test]
    public async Task 未指定なら既定フォルダを使う()
    {
        var locations = new StorageLocations();

        await Assert.That(locations.ResolvedSettingsDirectory).IsEqualTo(StorageLocations.DefaultDirectory);
        await Assert.That(locations.ResolvedConfigsDirectory).IsEqualTo(StorageLocations.DefaultDirectory);
        await Assert.That(locations.ResolvedLogDirectory).IsEqualTo(StorageLocations.DefaultLogDirectory);
    }

    [Test]
    public async Task 空白の指定は未指定として扱う()
    {
        // 入力欄を消しただけで壊れた場所を指しに行かないこと
        var locations = new StorageLocations { ConfigsDirectory = "   " };

        await Assert.That(locations.ResolvedConfigsDirectory).IsEqualTo(StorageLocations.DefaultDirectory);
    }

    [Test]
    public async Task 保存すると読み直せる()
    {
        using var temp = new TempDir();
        var store = new StorageLocationsStore(temp.File("locations.json"));

        store.Save(new StorageLocations { LogDirectory = temp.File("logs") });

        var reloaded = new StorageLocationsStore(temp.File("locations.json")).Load();
        await Assert.That(reloaded.LogDirectory).IsEqualTo(temp.File("logs"));
    }

    [Test]
    public async Task 保存先を変えると既存ファイルが移動する()
    {
        using var temp = new TempDir();
        var from = Path.Combine(temp.Path, "before");
        var to = Path.Combine(temp.Path, "after");
        Directory.CreateDirectory(from);
        File.WriteAllText(Path.Combine(from, StorageLocations.ConfigsFileName), "{}");

        var store = new StorageLocationsStore(temp.File("locations.json"));
        store.Save(new StorageLocations { ConfigsDirectory = from });

        var problems = store.Save(new StorageLocations { ConfigsDirectory = to });

        await Assert.That(problems).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(to, StorageLocations.ConfigsFileName))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(from, StorageLocations.ConfigsFileName))).IsFalse();
    }

    [Test]
    public async Task 移動先に同名のファイルがあれば上書きせず理由を返す()
    {
        using var temp = new TempDir();
        var from = Path.Combine(temp.Path, "before");
        var to = Path.Combine(temp.Path, "after");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(from, StorageLocations.ConfigsFileName), "{}");
        File.WriteAllText(Path.Combine(to, StorageLocations.ConfigsFileName), "既存");

        var store = new StorageLocationsStore(temp.File("locations.json"));
        store.Save(new StorageLocations { ConfigsDirectory = from });

        var problems = store.Save(new StorageLocations { ConfigsDirectory = to });

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(File.ReadAllText(Path.Combine(to, StorageLocations.ConfigsFileName))).IsEqualTo("既存");
        // 指定自体は保存されているので、次回起動時は新しい場所を見る
        await Assert.That(new StorageLocationsStore(temp.File("locations.json")).Load().ConfigsDirectory)
            .IsEqualTo(to);
    }

    [Test]
    public async Task 壊れたブートストラップは既定値で続行する()
    {
        using var temp = new TempDir();
        File.WriteAllText(temp.File("locations.json"), "{ not json");

        var loaded = new StorageLocationsStore(temp.File("locations.json")).Load();

        await Assert.That(loaded.ConfigsDirectory).IsNull();
    }
}
