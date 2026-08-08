using System.Diagnostics;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Aws;

/// <summary>
/// SDK 内蔵の SSO デバイス認可フローでログインする。
/// 取得したトークンは AWS CLI と同じ ~/.aws/sso/cache に保存されるため、
/// アプリでログインすれば aws コマンド側もログイン済みになる (逆も同様)。
/// </summary>
public sealed class SsoLoginService : ISsoLoginService
{
    public async Task<ErrorDetail?> LoginAsync(ProfileName profile, CancellationToken cancellationToken = default)
    {
        var chain = new CredentialProfileStoreChain();
        if (!chain.TryGetAWSCredentials(profile.Value, out var credentials))
        {
            return new ErrorDetail(
                FailurePhase.Credentials,
                "ProfileNotFound",
                $"プロファイル「{profile.Value}」が見つかりません。~/.aws/config を確認してください。");
        }

        if (credentials is not SSOAWSCredentials sso)
        {
            return new ErrorDetail(
                FailurePhase.Credentials,
                "NotSsoProfile",
                $"プロファイル「{profile.Value}」は SSO プロファイルではないため、アプリ内ログインは使えません。");
        }

        // トークンが無い / 期限切れの場合、SDK がデバイス認可フローを開始し
        // このコールバック経由で承認ページをブラウザで開く
        sso.Options.SsoVerificationCallback = args =>
        {
            var url = string.IsNullOrEmpty(args.VerificationUriComplete)
                ? args.VerificationUri
                : args.VerificationUriComplete;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };

        try
        {
            // GetCredentials がログイン完了 (ユーザーのブラウザ承認 + ポーリング) までブロックする
            await Task.Run(() => sso.GetCredentials(), cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AwsErrors.Classify(ex, FailurePhase.Credentials, profile, isSsoProfile: true);
        }
    }
}
