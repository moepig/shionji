using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Aws;

namespace Shionji.Infrastructure.Tests;

/// <summary>
/// ブラウザ承認そのものは実 IdP がないと通せないが、その手前のガード節は検証できる。
/// ここを誤ると、ボタンを押しても何も起きない / 無関係なプロファイルでブラウザが開く。
/// </summary>
public class SsoLoginServiceTests
{
    private static ProfileName Profile(string name) => ProfileName.Create(name).Value;

    [Test]
    public async Task 存在しないプロファイルはProfileNotFound()
    {
        using var dir = new TempDir();
        var credentials = dir.File("credentials");
        await File.WriteAllTextAsync(credentials, """
            [existing]
            aws_access_key_id = AKIAIOSFODNN7EXAMPLE
            aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
            """);

        var error = await new SsoLoginService(credentials).LoginAsync(Profile("missing"));

        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Phase).IsEqualTo(FailurePhase.Credentials);
        await Assert.That(error.Code).IsEqualTo("ProfileNotFound");
    }

    [Test]
    public async Task SSOでないプロファイルはブラウザを開かずに断る()
    {
        using var dir = new TempDir();
        var credentials = dir.File("credentials");
        await File.WriteAllTextAsync(credentials, """
            [static-keys]
            aws_access_key_id = AKIAIOSFODNN7EXAMPLE
            aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
            """);

        var error = await new SsoLoginService(credentials).LoginAsync(Profile("static-keys"));

        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Code).IsEqualTo("NotSsoProfile");
        await Assert.That(error.Message).Contains("static-keys");
    }

    [Test]
    public async Task SSOプロファイルの判定がクライアントファクトリと一致する()
    {
        using var dir = new TempDir();
        var credentials = dir.File("credentials");
        await File.WriteAllTextAsync(credentials, """
            [static-keys]
            aws_access_key_id = AKIAIOSFODNN7EXAMPLE
            aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY

            [sso-profile]
            sso_start_url = https://example.awsapps.com/start
            sso_region = ap-northeast-1
            sso_account_id = 123456789012
            sso_role_name = MyRole
            region = ap-northeast-1
            """);
        var factory = new AwsClientFactory(profilesLocation: credentials);

        await Assert.That(factory.IsSsoProfile(Profile("static-keys"))).IsFalse();
        await Assert.That(factory.IsSsoProfile(Profile("sso-profile"))).IsTrue();
        await Assert.That(factory.IsSsoProfile(Profile("missing"))).IsFalse();
    }
}
