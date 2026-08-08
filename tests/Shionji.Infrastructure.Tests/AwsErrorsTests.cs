using Amazon.Runtime;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Aws;

namespace Shionji.Infrastructure.Tests;

public class AwsErrorsTests
{
    private static ProfileName Profile() => ProfileName.Create("prod-sso").Value;

    [Test]
    public async Task SSO系の例外はCredentialsに分類されプロファイル名を案内する()
    {
        var error = AwsErrors.Classify(
            new AmazonClientException("Failed to get SSO token. Session has expired."),
            FailurePhase.ResolveDestination,
            Profile());

        await Assert.That(error.Phase).IsEqualTo(FailurePhase.Credentials);
        await Assert.That(error.Code).IsEqualTo("SsoLoginRequired");
        await Assert.That(error.Message).Contains("aws sso login --profile prod-sso");
    }

    [Test]
    public async Task アクセス拒否はPermissionに分類される()
    {
        var exception = new AmazonServiceException(
            "not allowed", ErrorType.Unknown, "AccessDeniedException", "req-1", System.Net.HttpStatusCode.BadRequest);

        var error = AwsErrors.Classify(exception, FailurePhase.ResolveDestination, Profile());

        await Assert.That(error.Phase).IsEqualTo(FailurePhase.Permission);
    }

    [Test]
    public async Task その他のサービス例外は既定フェーズを使う()
    {
        var exception = new AmazonServiceException(
            "throttled", ErrorType.Unknown, "ThrottlingException", "req-1", System.Net.HttpStatusCode.BadRequest);

        var error = AwsErrors.Classify(exception, FailurePhase.ResolveGateway, Profile());

        await Assert.That(error.Phase).IsEqualTo(FailurePhase.ResolveGateway);
        await Assert.That(error.Code).IsEqualTo("ThrottlingException");
    }
}
