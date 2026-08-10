using Microsoft.Win32;

namespace Shionji.App.WinUI;

/// <summary>
/// Windows へのサインイン時の自動起動。現在のユーザーの Run キーへの登録で表す。
/// 登録の有無が状態そのものであり、アプリ設定ファイルには持たない
/// (タスクマネージャーなど OS 側から解除された場合に食い違わないようにするため)。
/// </summary>
/// <param name="executablePath">登録する実行ファイル。省略時は動作中の実行ファイル。</param>
/// <param name="keyPath">登録先のキー。省略時は現在のユーザーの Run キー。</param>
public sealed class WindowsAutoStart(string? executablePath = null, string? keyPath = null)
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Run キーでの項目名。表示名としてタスクマネージャーにも出る。</summary>
    private const string ValueName = "Shionji";

    private readonly string _executablePath = executablePath ?? Environment.ProcessPath ?? string.Empty;
    private readonly string _keyPath = keyPath ?? RunKeyPath;

    /// <summary>登録済みか。読めない場合は未登録として扱う。</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
                return key?.GetValue(ValueName) is not null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 登録 / 解除する。既に望みの状態であっても、登録先は書き直す
    /// (実行ファイルを別の場所へ移した後でも追従させるため)。
    /// </summary>
    /// <returns>変更できなかった場合はその理由。変更できた場合は null。</returns>
    public string? SetEnabled(bool enabled)
    {
        if (enabled && _executablePath.Length == 0)
            return "自動起動: 実行ファイルの場所を特定できません。";

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
            if (enabled)
                key.SetValue(ValueName, $"\"{_executablePath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception ex)
            when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return $"自動起動の設定を変更できません: {ex.Message}";
        }
    }
}
