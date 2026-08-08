using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Ports;

/// <summary>
/// SSO (IAM Identity Center) のブラウザ承認込みログイン。
/// ユーザー操作を起点にのみ呼び出すこと (バックグラウンド解決から呼ぶとブラウザが突然開く)。
/// </summary>
public interface ISsoLoginService
{
    /// <summary>ログインを行い完了まで待つ。</summary>
    /// <returns>成功時は null、失敗時はエラー詳細。</returns>
    Task<ErrorDetail?> LoginAsync(ProfileName profile, CancellationToken cancellationToken = default);
}
