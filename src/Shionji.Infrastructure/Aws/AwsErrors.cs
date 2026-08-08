using System.Net;
using Amazon.Runtime;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Aws;

/// <summary>AWS SDK の例外をフェーズ付きエラーへ分類する。</summary>
public static class AwsErrors
{
    /// <param name="isSsoProfile">
    /// 対象プロファイルが SSO かどうか。資格情報エラー時の案内文言を出し分ける
    /// (SSO でないプロファイルに aws sso login を案内すると誤誘導になる)。
    /// </param>
    public static ErrorDetail Classify(
        Exception exception, FailurePhase defaultPhase, ProfileName profile, bool isSsoProfile)
    {
        if (IsCredentialFailure(exception))
        {
            return isSsoProfile
                ? new ErrorDetail(
                    FailurePhase.Credentials,
                    "SsoLoginRequired",
                    $"プロファイル「{profile.Value}」の認証情報が無効か期限切れです。" +
                    $"「SSO ログイン」ボタンか `aws sso login --profile {profile.Value}` でログインしてください。")
                : new ErrorDetail(
                    FailurePhase.Credentials,
                    "CredentialsInvalid",
                    $"プロファイル「{profile.Value}」の資格情報が無効か期限切れです。" +
                    "~/.aws/credentials / ~/.aws/config の設定を確認してください。");
        }

        if (exception is AmazonServiceException service)
        {
            if (service.StatusCode == HttpStatusCode.Forbidden ||
                service.ErrorCode is "AccessDenied" or "AccessDeniedException" or "UnauthorizedOperation")
            {
                return new ErrorDetail(
                    FailurePhase.Permission,
                    service.ErrorCode ?? "AccessDenied",
                    $"AWS API の権限が不足しています: {service.Message}");
            }

            return new ErrorDetail(defaultPhase, service.ErrorCode ?? service.GetType().Name, service.Message);
        }

        return new ErrorDetail(defaultPhase, exception.GetType().Name, exception.Message);
    }

    /// <summary>
    /// 資格情報起因の失敗の判定。誤分類は誤った対処法の案内につながるため、
    /// SDK の SSO 系例外型と既知のエラーコードに限定する
    /// (例外型名やメッセージへの緩い部分一致は使わない)。
    /// </summary>
    private static bool IsCredentialFailure(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException!)
        {
            var type = e.GetType();

            // AWSSDK.SSO / SSOOIDC の例外 (トークン取得・更新の失敗)
            if (type.Namespace?.StartsWith("Amazon.SSO", StringComparison.Ordinal) == true)
                return true;

            // SDK のトークンプロバイダ例外 (SSOTokenProviderException など)
            if (type.Name.Contains("SSOToken", StringComparison.OrdinalIgnoreCase))
                return true;

            // 資格情報の無効・期限切れを示す既知のサービスエラーコード
            if (e is AmazonServiceException service &&
                service.ErrorCode is "ExpiredToken" or "ExpiredTokenException" or "RequestExpired"
                    or "InvalidClientTokenId" or "UnrecognizedClientException")
            {
                return true;
            }

            // SDK が SSO トークン解決失敗を AmazonClientException で包むケース
            if (e is AmazonClientException &&
                e.Message.Contains("SSO", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
