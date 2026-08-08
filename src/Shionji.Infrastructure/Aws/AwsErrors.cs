using System.Net;
using Amazon.Runtime;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Aws;

/// <summary>AWS SDK の例外をフェーズ付きエラーへ分類する。</summary>
public static class AwsErrors
{
    public static ErrorDetail Classify(Exception exception, FailurePhase defaultPhase, ProfileName profile)
    {
        if (IsCredentialFailure(exception))
        {
            return new ErrorDetail(
                FailurePhase.Credentials,
                "SsoLoginRequired",
                $"プロファイル「{profile.Value}」の認証情報が無効か期限切れです。" +
                $"`aws sso login --profile {profile.Value}` を実行してください。");
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
    /// SSO トークン期限切れなど資格情報起因の失敗かどうかのヒューリスティック。
    /// SDK は SSO 系の例外型 (SSOTokenProviderException など) や
    /// 「トークンを取得できない」旨のメッセージを含む AmazonClientException を投げる。
    /// </summary>
    private static bool IsCredentialFailure(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException!)
        {
            var typeName = e.GetType().Name;
            if (typeName.Contains("SSO", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("Token", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (e is AmazonClientException &&
                (e.Message.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("sso", StringComparison.OrdinalIgnoreCase) ||
                 e.Message.Contains("token", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
