using Shionji.Infrastructure.Storage;

namespace Shionji.Infrastructure.Tests;

/// <summary>
/// 保存先の指定はアプリ設定に同居する。
/// アプリ設定ファイル自身の置き場所だけは固定 (自分の置き場所は自分に書けない)。
/// </summary>
public class StorageLocationsTests
{
    [Test]
    public async Task 未指定なら既定フォルダを使う()
    {
        var settings = new AppSettings();

        await Assert.That(AppPaths.ResolveLogDirectory(settings)).IsEqualTo(AppPaths.DefaultLogDirectory);
        await Assert.That(AppPaths.ResolveConfigsDirectory(settings)).IsEqualTo(AppPaths.DefaultDirectory);
    }

    [Test]
    public async Task 空白の指定は未指定として扱う()
    {
        // 入力欄を消しただけで壊れた場所を指しに行かないこと
        var settings = new AppSettings { ConfigsDirectory = "   " };

        await Assert.That(AppPaths.ResolveConfigsDirectory(settings)).IsEqualTo(AppPaths.DefaultDirectory);
    }

    [Test]
    public async Task 既定と同じ場所を指定したら上書きを持たない()
    {
        // 既定が変わったときに追従できるようにする
        await Assert.That(AppPaths.NormalizeDirectory(AppPaths.DefaultDirectory, AppPaths.DefaultDirectory))
            .IsNull();
        await Assert.That(AppPaths.NormalizeDirectory("  ", AppPaths.DefaultDirectory)).IsNull();
        await Assert.That(AppPaths.NormalizeDirectory(@"D:\別\", AppPaths.DefaultDirectory)).IsEqualTo(@"D:\別");
    }

    [Test]
    public async Task 保存すると読み直せる()
    {
        using var temp = new TempDir();
        var store = new AppSettingsStore(temp.File(AppPaths.SettingsFileName));

        store.Save(new AppSettings { LogDirectory = temp.File("logs"), Theme = "Dark" });

        var reloaded = new AppSettingsStore(temp.File(AppPaths.SettingsFileName)).Load();
        await Assert.That(reloaded.LogDirectory).IsEqualTo(temp.File("logs"));
        await Assert.That(reloaded.Theme).IsEqualTo("Dark");
    }

    [Test]
    public async Task 接続先設定の保存先を変えると既存ファイルが移動する()
    {
        using var temp = new TempDir();
        var from = Path.Combine(temp.Path, "before");
        var to = Path.Combine(temp.Path, "after");
        Directory.CreateDirectory(from);
        File.WriteAllText(Path.Combine(from, AppPaths.ConfigsFileName), "{}");

        var store = new AppSettingsStore(temp.File(AppPaths.SettingsFileName));
        store.Save(new AppSettings { ConfigsDirectory = from });

        var problems = store.Save(new AppSettings { ConfigsDirectory = to });

        await Assert.That(problems).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(to, AppPaths.ConfigsFileName))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(from, AppPaths.ConfigsFileName))).IsFalse();
    }

    [Test]
    public async Task 移動先に同名のファイルがあれば上書きせず理由を返す()
    {
        using var temp = new TempDir();
        var from = Path.Combine(temp.Path, "before");
        var to = Path.Combine(temp.Path, "after");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(from, AppPaths.ConfigsFileName), "{}");
        File.WriteAllText(Path.Combine(to, AppPaths.ConfigsFileName), "既存");

        var store = new AppSettingsStore(temp.File(AppPaths.SettingsFileName));
        store.Save(new AppSettings { ConfigsDirectory = from });

        var problems = store.Save(new AppSettings { ConfigsDirectory = to });

        await Assert.That(problems.Count).IsEqualTo(1);
        await Assert.That(File.ReadAllText(Path.Combine(to, AppPaths.ConfigsFileName))).IsEqualTo("既存");
        // 指定自体は保存されているので、次回起動時は新しい場所を見る
        await Assert.That(new AppSettingsStore(temp.File(AppPaths.SettingsFileName)).Load().ConfigsDirectory)
            .IsEqualTo(to);
    }

    [Test]
    public async Task 設定画面で触らない項目は保存しても残る()
    {
        // 設定画面はテーマと保存先しか扱わない。with で複製することで、
        // 画面に無い項目 (plugin パスなど) が既定値に戻らないことを保証する
        using var temp = new TempDir();
        var path = temp.File(AppPaths.SettingsFileName);
        var store = new AppSettingsStore(path);
        store.Save(new AppSettings
        {
            PluginPath = @"C:\tools\session-manager-plugin.exe",
            AwsEndpointOverride = "https://ssm.internal",
            HideOnMinimize = false,
            LogRetentionDays = 400,
        });

        // 設定画面の保存に相当する操作
        store.Save(store.Current with { Theme = "Dark", LogDirectory = temp.File("logs") });

        var reloaded = new AppSettingsStore(path).Load();
        await Assert.That(reloaded.PluginPath).IsEqualTo(@"C:\tools\session-manager-plugin.exe");
        await Assert.That(reloaded.AwsEndpointOverride).IsEqualTo("https://ssm.internal");
        await Assert.That(reloaded.HideOnMinimize).IsFalse();
        await Assert.That(reloaded.LogRetentionDays).IsEqualTo(400);
        await Assert.That(reloaded.Theme).IsEqualTo("Dark");
        await Assert.That(reloaded.LogDirectory).IsEqualTo(temp.File("logs"));
    }
}
