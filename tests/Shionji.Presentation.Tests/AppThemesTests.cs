namespace Shionji.Presentation.Tests;

/// <summary>
/// テーマは設定ファイルに文字列で入る。手で編集されたり、
/// 将来の版で書かれた値が入っていても落ちないこと。
/// </summary>
public class AppThemesTests
{
    [Test]
    [Arguments("System", AppTheme.System)]
    [Arguments("Light", AppTheme.Light)]
    [Arguments("Dark", AppTheme.Dark)]
    [Arguments("dark", AppTheme.Dark)]
    [Arguments("DARK", AppTheme.Dark)]
    public async Task 保存されている文字列を読める(string stored, AppTheme expected) =>
        await Assert.That(AppThemes.Parse(stored)).IsEqualTo(expected);

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("  ")]
    [Arguments("Sepia")]
    [Arguments("99")]
    [Arguments("-1")]
    public async Task 読めない値はシステム追従にする(string? stored) =>
        await Assert.That(AppThemes.Parse(stored)).IsEqualTo(AppTheme.System);

    [Test]
    public async Task 書き出した文字列は読み戻せる()
    {
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            var stored = AppThemes.ToStorageValue(theme);
            await Assert.That(AppThemes.Parse(stored)).IsEqualTo(theme);
        }
    }
}
