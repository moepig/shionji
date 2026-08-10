using Shionji.Infrastructure.Storage;

namespace Shionji.Infrastructure.Tests;

public class AppSettingsStoreTests
{
    [Test]
    public async Task ファイルが無ければ既定値を返す()
    {
        using var dir = new TempDir();
        var store = new AppSettingsStore(dir.File("appsettings.json"));

        var settings = store.Load();

        await Assert.That(settings.HideOnMinimize).IsTrue();
        await Assert.That(settings.PluginPath).IsNull();
        await Assert.That(settings.AwsEndpointOverride).IsNull();
    }

    [Test]
    public async Task 保存した設定を読み込める()
    {
        using var dir = new TempDir();
        var path = dir.File("appsettings.json");
        new AppSettingsStore(path).Save(new AppSettings
        {
            PluginPath = @"C:\tools\session-manager-plugin.exe",
            AwsEndpointOverride = "https://ssm.internal",
        });

        var loaded = new AppSettingsStore(path).Load();

        await Assert.That(loaded.PluginPath).IsEqualTo(@"C:\tools\session-manager-plugin.exe");
        await Assert.That(loaded.AwsEndpointOverride).IsEqualTo("https://ssm.internal");
    }

    [Test]
    public async Task タスクトレイの既定は最小化したときだけ格納する()
    {
        using var dir = new TempDir();

        var settings = new AppSettingsStore(dir.File("appsettings.json")).Load();

        await Assert.That(settings.HideOnMinimize).IsTrue();
        await Assert.That(settings.StartMinimized).IsFalse();
    }

    [Test]
    public async Task 終了時の確認は既定で出す()
    {
        using var dir = new TempDir();

        var settings = new AppSettingsStore(dir.File("appsettings.json")).Load();

        await Assert.That(settings.ConfirmOnExit).IsTrue();
    }

    [Test]
    public async Task タスクトレイと起動と終了の指定を読み書きできる()
    {
        using var dir = new TempDir();
        var path = dir.File("appsettings.json");
        new AppSettingsStore(path).Save(new AppSettings
        {
            HideOnMinimize = false,
            StartMinimized = true,
            ConfirmOnExit = false,
        });

        var loaded = new AppSettingsStore(path).Load();

        await Assert.That(loaded.HideOnMinimize).IsFalse();
        await Assert.That(loaded.StartMinimized).IsTrue();
        await Assert.That(loaded.ConfirmOnExit).IsFalse();
    }

    [Test]
    public async Task 保存時に親ディレクトリを作る()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "nested", "deeper", "appsettings.json");

        new AppSettingsStore(path).Save(new AppSettings());

        await Assert.That(File.Exists(path)).IsTrue();
    }

    [Test]
    public async Task 壊れたJSONでも既定値で起動できる()
    {
        // 設定ファイルの不備でアプリが起動不能にならないこと
        using var dir = new TempDir();
        var path = dir.File("appsettings.json");
        await File.WriteAllTextAsync(path, "{ this is not json");

        var settings = new AppSettingsStore(path).Load();

        await Assert.That(settings.HideOnMinimize).IsTrue();
    }

    [Test]
    public async Task null本体でも既定値になる()
    {
        using var dir = new TempDir();
        var path = dir.File("appsettings.json");
        await File.WriteAllTextAsync(path, "null");

        var settings = new AppSettingsStore(path).Load();

        await Assert.That(settings.HideOnMinimize).IsTrue();
    }

    [Test]
    public async Task 廃止したキーが残っていても読める()
    {
        // 閉じたときの格納 (MinimizeToTray) は廃止した。
        // 以前のバージョンが書いたファイルでも、残りの項目がそのまま効くこと
        using var dir = new TempDir();
        var path = dir.File("appsettings.json");
        await File.WriteAllTextAsync(path, """{ "MinimizeToTray": false, "HideOnMinimize": false }""");

        var settings = new AppSettingsStore(path).Load();

        await Assert.That(settings.HideOnMinimize).IsFalse();
        await Assert.That(settings.ConfirmOnExit).IsTrue();
    }

    [Test]
    public async Task 未知のプロパティがあっても読める()
    {
        // 将来バージョンで書かれたファイルを読んでも落ちない
        using var dir = new TempDir();
        var path = dir.File("appsettings.json");
        await File.WriteAllTextAsync(path, """{ "HideOnMinimize": false, "FutureSetting": 42 }""");

        var settings = new AppSettingsStore(path).Load();

        await Assert.That(settings.HideOnMinimize).IsFalse();
    }

    [Test]
    public async Task Currentは最後に読み書きした内容を返す()
    {
        using var dir = new TempDir();
        var store = new AppSettingsStore(dir.File("appsettings.json"));

        store.Save(new AppSettings { PluginPath = "a" });
        await Assert.That(store.Current.PluginPath).IsEqualTo("a");

        store.Save(new AppSettings { PluginPath = "b" });
        await Assert.That(store.Current.PluginPath).IsEqualTo("b");
    }
}
