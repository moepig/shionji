using Amazon.Runtime;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Aws;

namespace Shionji.Infrastructure.Tests;

public class AwsErrorsTests
{
    private static ProfileName Profile() => ProfileName.Create("prod-sso").Value;

    [Test]
    public async Task SSOトークン失効はSSOプロファイルならログイン案内になる()
    {
        var error = AwsErrors.Classify(
            new AmazonClientException("Failed to get SSO token. Session has expired."),
            FailurePhase.ResolveDestination,
            Profile(),
            isSsoProfile: true);

        await Assert.That(error.Phase).IsEqualTo(FailurePhase.Credentials);
        await Assert.That(error.Code).IsEqualTo("SsoLoginRequired");
        await Assert.That(error.Message).Contains("aws sso login --profile prod-sso");
    }

    [Test]
    public async Task 資格情報エラーでも非SSOプロファイルにはssoログインを案内しない()
    {
        var exception = new AmazonServiceException(
            "The security token included in the request is invalid.",
            ErrorType.Sender, "InvalidClientTokenId", "req-1", System.Net.HttpStatusCode.Forbidden);

        var error = AwsErrors.Classify(
            exception, FailurePhase.ResolveDestination, Profile(), isSsoProfile: false);

        await Assert.That(error.Phase).IsEqualTo(FailurePhase.Credentials);
        await Assert.That(error.Code).IsEqualTo("CredentialsInvalid");
        await Assert.That(error.Message.Contains("sso login")).IsFalse();
    }

    [Test]
    public async Task 型名やメッセージにtokenを含むだけでは資格情報エラーにしない()
    {
        var error = AwsErrors.Classify(
            new InvalidOperationException("cancellation token was disposed"),
            FailurePhase.ResolveGateway,
            Profile(),
            isSsoProfile: true);

        await Assert.That(error.Phase).IsEqualTo(FailurePhase.ResolveGateway);
    }

    [Test]
    public async Task アクセス拒否はPermissionに分類される()
    {
        var exception = new AmazonServiceException(
            "not allowed", ErrorType.Unknown, "AccessDeniedException", "req-1", System.Net.HttpStatusCode.BadRequest);

        var error = AwsErrors.Classify(
            exception, FailurePhase.ResolveDestination, Profile(), isSsoProfile: true);

        await Assert.That(error.Phase).IsEqualTo(FailurePhase.Permission);
    }

    [Test]
    public async Task その他のサービス例外は既定フェーズを使う()
    {
        var exception = new AmazonServiceException(
            "throttled", ErrorType.Unknown, "ThrottlingException", "req-1", System.Net.HttpStatusCode.BadRequest);

        var error = AwsErrors.Classify(
            exception, FailurePhase.ResolveGateway, Profile(), isSsoProfile: true);

        await Assert.That(error.Phase).IsEqualTo(FailurePhase.ResolveGateway);
        await Assert.That(error.Code).IsEqualTo("ThrottlingException");
    }

    [Test]
    public async Task SSO名前空間の例外は内側にあっても資格情報エラーとして拾う()
    {
        var inner = new Amazon.SSOOIDC.Model.ExpiredTokenException("expired");
        var error = AwsErrors.Classify(
            new AmazonClientException("wrapper", inner),
            FailurePhase.ResolveDestination,
            Profile(),
            isSsoProfile: true);

        await Assert.That(error.Phase).IsEqualTo(FailurePhase.Credentials);
        await Assert.That(error.Code).IsEqualTo("SsoLoginRequired");
    }
}
