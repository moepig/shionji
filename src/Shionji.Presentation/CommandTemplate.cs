namespace Shionji.Presentation;

/// <summary>
/// 登録済みコマンドの文字列処理。プレースホルダの差し込みと、起動に渡す形への分解を行う。
/// 実行そのものは <see cref="IExternalCommandLauncher"/> が担う。
/// </summary>
public static class CommandTemplate
{
    /// <summary>待ち受けているローカル側のホストに置き換わる。</summary>
    public const string HostPlaceholder = "{host}";

    /// <summary>待ち受けているローカル側のポート番号に置き換わる。</summary>
    public const string PortPlaceholder = "{port}";

    /// <summary>
    /// プレースホルダを実際の値へ差し込む。大文字と小文字は区別しない。
    /// 該当しない箇所は書かれたまま残す。
    /// </summary>
    public static string Expand(string commandLine, string host, int port) =>
        commandLine
            .Replace(HostPlaceholder, host, StringComparison.OrdinalIgnoreCase)
            .Replace(PortPlaceholder, port.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// コマンド行を実行ファイルと引数に分ける。先頭の 1 語を実行ファイルとし、
    /// 引用符で囲まれている場合はその内側までを 1 語として扱う (空白を含むパスのため)。
    /// </summary>
    /// <returns>実行ファイルと、それに続く残り全体。残りが無ければ引数は空文字。</returns>
    public static (string FileName, string Arguments) Split(string commandLine)
    {
        var text = commandLine.Trim();
        if (text.Length == 0)
            return (string.Empty, string.Empty);

        if (text[0] == '"')
        {
            var closing = text.IndexOf('"', 1);
            if (closing > 0)
                return (text[1..closing], text[(closing + 1)..].TrimStart());

            // 閉じ引用符が無い場合は、囲もうとした全体を実行ファイルとみなす
            return (text[1..], string.Empty);
        }

        var space = text.IndexOf(' ');
        return space < 0 ? (text, string.Empty) : (text[..space], text[(space + 1)..].TrimStart());
    }
}
