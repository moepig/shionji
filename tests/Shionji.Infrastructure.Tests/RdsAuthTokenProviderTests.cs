using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Aws;
using Shionji.Infrastructure.Fakes;

namespace Shionji.Infrastructure.Tests;

public class RdsAuthTokenProviderTests
{
    private const string Host = "database.cluster-example.ap-northeast-1.rds.amazonaws.com";
    private static AwsContext Aws(string profile = "database") =>
        new(ProfileName.Create(profile).Value, AwsRegion.Create("ap-northeast-1").Value);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task 指定プロファイルとDB接続先で15分間のトークンを署名する(bool sessionCredentials)
    {
        using var dir = new TempDir();
        var path = dir.File("credentials");
        await File.WriteAllTextAsync(path, """
            [default]
            aws_access_key_id = OTHEREXAMPLE
            aws_secret_access_key = other-example-secret
            [database]
            aws_access_key_id = AKIAIOSFODNN7EXAMPLE
            aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
            region = us-east-1
            """ + (sessionCredentials ? "\naws_session_token = example-session-token" : ""));
        var provider = new RdsAuthTokenProvider(new AwsClientFactory(
            endpointOverride: "http://localhost:12345", profilesLocation: path));

        var result = await provider.GenerateAsync(Aws(), HostName.Create(Host).Value,
            Port.Create(5432).Value, DatabaseUserName.Create("App_User+test").Value);

        await Assert.That(result.IsSuccess).IsTrue();
        var uri = new Uri("https://" + result.Value);
        var query = uri.Query.TrimStart('?').Split('&').Select(item => item.Split('=', 2))
            .ToDictionary(item => Uri.UnescapeDataString(item[0]), item => Uri.UnescapeDataString(item[1]));
        await Assert.That(uri.Host).IsEqualTo(Host);
        await Assert.That(uri.Port).IsEqualTo(5432);
        await Assert.That(query["Action"]).IsEqualTo("connect");
        await Assert.That(query["DBUser"]).IsEqualTo("App_User+test");
        await Assert.That(query["X-Amz-Expires"]).IsEqualTo("900");
        await Assert.That(query["X-Amz-Algorithm"]).IsEqualTo("AWS4-HMAC-SHA256");
        await Assert.That(query["X-Amz-Credential"]).StartsWith("AKIAIOSFODNN7EXAMPLE/");
        await Assert.That(query["X-Amz-Credential"]).Contains("/ap-northeast-1/rds-db/aws4_request");
        await Assert.That(query["X-Amz-Signature"].Length).IsEqualTo(64);
        await Assert.That(query.ContainsKey("X-Amz-Security-Token")).IsEqualTo(sessionCredentials);
        if (sessionCredentials)
            await Assert.That(query["X-Amz-Security-Token"]).IsEqualTo("example-session-token");
    }

    [Test]
    public async Task プロファイルがなければ認証エラーで既定資格情報にフォールバックしない()
    {
        using var dir = new TempDir();
        var path = dir.File("credentials");
        await File.WriteAllTextAsync(path, "");
        var provider = new RdsAuthTokenProvider(new AwsClientFactory(profilesLocation: path));
        var result = await provider.GenerateAsync(Aws("missing"), HostName.Create(Host).Value,
            Port.Create(3306).Value, DatabaseUserName.Create("app").Value);

        await Assert.That(result.Error.Phase).IsEqualTo(FailurePhase.Credentials);
        await Assert.That(result.Error.Code).IsEqualTo("ProfileNotFound");
    }

    [Test]
    public async Task デモでは実際のトークンを生成しない()
    {
        var result = await new FakeRdsAuthTokenProvider().GenerateAsync(Aws(), HostName.Create(Host).Value,
            Port.Create(5432).Value, DatabaseUserName.Create("app").Value);
        await Assert.That(result.Error.Code).IsEqualTo("DemoMode");
    }
}
