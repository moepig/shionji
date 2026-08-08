namespace Shionji.Domain.Diagnostics;

/// <summary>
/// 1 行のログが持つ「短い要約」と「詳細フィールド」。
/// 画面のステータスバーは要約だけを出し、テキストログは詳細まで展開する。
/// </summary>
public interface IDetailedLogState
{
    /// <summary>そのまま画面に出せる 1 行の要約。</summary>
    string Summary { get; }

    /// <summary>監査用の詳細。key=値 の形で記録される。</summary>
    IReadOnlyList<KeyValuePair<string, object?>> Details { get; }
}
