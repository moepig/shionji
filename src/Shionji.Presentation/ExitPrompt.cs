namespace Shionji.Presentation;

/// <summary>
/// 終了の確認に出す内容。ダイアログの見た目は各 UI に任せ、
/// 確認の要否と文面だけをここで決める。
/// </summary>
/// <param name="Title">見出し。</param>
/// <param name="Message">本文。切断されるものがあるかどうかで変わる。</param>
public sealed record ExitPrompt(string Title, string Message)
{
    /// <summary>
    /// 終了の確認に出す内容を作る。
    /// </summary>
    /// <param name="confirmOnExit">確認を出す設定か。</param>
    /// <param name="connectedCount">接続中のセッション数。何が失われるかを示すために出す。</param>
    /// <returns>確認を出す場合はその内容。出さずに終了してよい場合は null。</returns>
    public static ExitPrompt? For(bool confirmOnExit, int connectedCount)
    {
        if (!confirmOnExit)
            return null;

        return new ExitPrompt(
            "Shionji を終了しますか?",
            connectedCount > 0
                ? $"接続中の {connectedCount} 件を切断して終了します。"
                : "常駐を終了します。タスクトレイからも消えます。");
    }
}
