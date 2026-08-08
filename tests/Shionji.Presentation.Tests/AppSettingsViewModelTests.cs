namespace Shionji.Presentation.Tests;

/// <summary>アプリ設定ウィンドウ (接続先設定とは別)。</summary>
public class AppSettingsViewModelTests
{
    [Test]
    public async Task ツールバーから開くと現在値が入っている()
    {
        var ui = new UiHarness();
        ui.AppSettings.Theme = AppTheme.Dark;
        ui.AppSettings.LogDirectory = @"D:\shionji\logs";
        ui.AppSettings.SettingsFilePath = @"D:\shionji\conf\appsettings.json";
        ui.AppSettings.ConfigsFilePath = @"D:\shionji\conf\configs.json";

        ui.Main.ShowSettingsCommand.Execute(null);

        var settings = ui.SettingsWindow.Last;
        await Assert.That(settings.Theme).IsEqualTo(AppTheme.Dark);
        await Assert.That(settings.LogDirectory).IsEqualTo(@"D:\shionji\logs");
        await Assert.That(settings.SettingsDirectory).IsEqualTo(@"D:\shionji\conf");
        await Assert.That(settings.ConfigsDirectory).IsEqualTo(@"D:\shionji\conf");
        await Assert.That(settings.ConfigsFileName).IsEqualTo("configs.json");
    }

    [Test]
    public async Task テーマは選んだ時点で見た目に反映される()
    {
        var settings = Open(out var fake);

        settings.Theme = AppTheme.Light;

        await Assert.That(fake.PreviewedTheme).IsEqualTo(AppTheme.Light);
    }

    [Test]
    public async Task キャンセルするとテーマは開いたときの値に戻る()
    {
        var ui = new UiHarness();
        ui.AppSettings.Theme = AppTheme.Dark;
        ui.Main.ShowSettingsCommand.Execute(null);
        var settings = ui.SettingsWindow.Last;

        settings.Theme = AppTheme.Light;
        settings.CancelCommand.Execute(null);

        await Assert.That(ui.AppSettings.PreviewedTheme).IsEqualTo(AppTheme.Dark);
        await Assert.That(ui.AppSettings.Saved).IsEmpty();
    }

    [Test]
    public async Task 保存すると入力どおりに渡る()
    {
        var settings = Open(out var fake);
        settings.Theme = AppTheme.Dark;
        settings.LogDirectory = @"E:\logs";
        settings.ConfigsDirectory = @"E:\conf";

        settings.SaveCommand.Execute(null);

        var saved = fake.Saved.Single();
        await Assert.That(saved.Theme).IsEqualTo(AppTheme.Dark);
        await Assert.That(saved.Log).IsEqualTo(@"E:\logs");
        await Assert.That(saved.Configs).IsEqualTo(@"E:\conf");
    }

    [Test]
    public async Task 保存先を変えたら再起動が要ることを伝えて開いたままにする()
    {
        var settings = Open(out _);
        var closed = 0;
        settings.Closed += (_, _) => closed++;

        settings.LogDirectory = @"E:\logs";
        settings.SaveCommand.Execute(null);

        await Assert.That(settings.NeedsRestart).IsTrue();
        await Assert.That(closed).IsEqualTo(0);
    }

    [Test]
    public async Task 保存先を変えていなければ保存して閉じる()
    {
        var settings = Open(out _);
        var closed = 0;
        settings.Closed += (_, _) => closed++;

        settings.Theme = AppTheme.Light;
        settings.SaveCommand.Execute(null);

        await Assert.That(settings.NeedsRestart).IsFalse();
        await Assert.That(closed).IsEqualTo(1);
    }

    [Test]
    public async Task 反映できなかった事情は閉じずに見せる()
    {
        var settings = Open(out var fake);
        fake.Problems.Add("接続先設定ファイル: 移動先に同名のファイルがあります。");
        var closed = 0;
        settings.Closed += (_, _) => closed++;

        settings.SaveCommand.Execute(null);

        await Assert.That(settings.HasProblems).IsTrue();
        await Assert.That(settings.Problems.Single()).Contains("同名のファイル");
        await Assert.That(closed).IsEqualTo(0);
    }

    [Test]
    public async Task 参照でフォルダを選ぶと入力欄に入る()
    {
        var ui = new UiHarness();
        ui.FolderPicker.NextFolder = @"F:\選んだ場所";
        ui.Main.ShowSettingsCommand.Execute(null);
        var settings = ui.SettingsWindow.Last;

        await settings.BrowseConfigsCommand.ExecuteAsync(null);

        await Assert.That(settings.ConfigsDirectory).IsEqualTo(@"F:\選んだ場所");
    }

    [Test]
    public async Task 参照をキャンセルしても入力欄は変わらない()
    {
        var ui = new UiHarness();
        ui.FolderPicker.NextFolder = null;
        ui.Main.ShowSettingsCommand.Execute(null);
        var settings = ui.SettingsWindow.Last;
        var before = settings.LogDirectory;

        await settings.BrowseLogCommand.ExecuteAsync(null);

        await Assert.That(settings.LogDirectory).IsEqualTo(before);
    }

    [Test]
    public async Task フォルダを開くのは入力中の場所に対して行う()
    {
        var ui = new UiHarness();
        ui.Main.ShowSettingsCommand.Execute(null);
        var settings = ui.SettingsWindow.Last;
        settings.ConfigsDirectory = @"G:\いま入力中";

        settings.OpenConfigsCommand.Execute(null);

        await Assert.That(ui.FileLocation.OpenedFolders.Single()).IsEqualTo(@"G:\いま入力中");
    }

    private static AppSettingsViewModel Open(out FakeAppSettings settings)
    {
        var ui = new UiHarness();
        settings = ui.AppSettings;
        ui.Main.ShowSettingsCommand.Execute(null);
        return ui.SettingsWindow.Last;
    }
}
