namespace Shionji.Domain.Diagnostics;

/// <summary>
/// 1 件のログが持つ「短い要約」と「詳細フィールド」。
/// 画面のステータスバーは要約だけを出し、ファイルログは詳細まで展開する。
/// 出力先ごとの整形は各ログプロバイダが担う。
/// </summary>
public interface IDetailedLogState
{
    /// <summary>そのまま画面に出せる 1 行の要約。</summary>
    string Summary { get; }

    /// <summary>監査用の詳細。キーと値の組として記録される。</summary>
    IReadOnlyList<KeyValuePair<string, object?>> Details { get; }
}
