using System.ComponentModel;
using Amazon;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Shionji.Domain.Ports;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure.Aws;

namespace Shionji.Infrastructure.Tests;

public class AwsAuthenticationServiceTests
{
    private const string AuthorizationUrl = "https://ap-northeast-1.signin.aws.amazon.com/v1/authorize?state=secret-state&code_challenge=secret-challenge";
    private static AwsContext Aws(string profile = "test") =>
        new(ProfileName.Create(profile).Value, AwsRegion.Create("ap-northeast-1").Value);

    private static AwsAuthenticationService LoginService(FakeCli cli, Action<Uri>? open = null) =>
        new(new AwsClientFactory(profilesLocation: "unused-test-credentials"), cli,
            open ?? (_ => { }), (_, _) => throw new InvalidOperationException("ログインでは STS を呼ばない"));

    [Test]
    public async Task 出力の認証URLを一度だけ開いてプロファイルを単一引数で渡す()
    {
        var cli = new FakeCli { Lines = ["https://docs.aws.amazon.com/", AuthorizationUrl, AuthorizationUrl] };
        var opened = new List<Uri>();
        var progress = new RecordingProgress();

        var error = await LoginService(cli, opened.Add).LoginAsync(Aws("dev with spaces & more"), progress);

        await Assert.That(error).IsNull();
        await Assert.That(opened.Count).IsEqualTo(1);
        await Assert.That(opened[0].AbsoluteUri).IsEqualTo(AuthorizationUrl);
        await Assert.That(cli.Arguments).IsEquivalentTo(new[]
        {
            "login", "--profile", "dev with spaces & more", "--region", "ap-northeast-1",
            "--no-cli-pager", "--no-cli-auto-prompt", "--color", "off",
        });
        await Assert.That(progress.Updates).IsEquivalentTo(new[]
        {
            new AwsLoginProgress(AwsLoginStage.OpeningBrowser),
            new AwsLoginProgress(AwsLoginStage.WaitingForApproval),
            new AwsLoginProgress(AwsLoginStage.ProcessExited, 0),
        });
    }

    [Test]
    [Arguments("http://signin.aws.amazon.com/authorize")]
    [Arguments("https://signin.aws.amazon.com.evil.test/authorize")]
    [Arguments("https://signin.aws.amazon.com@evil.test/authorize")]
    [Arguments("https://evil.test/signin.aws.amazon.com/authorize")]
    [Arguments("https://signin.aws.amazon.com:8443/authorize")]
    [Arguments("https://docs.aws.amazon.com/authorize")]
    [Arguments("https://signin.aws.amazon.com/help")]
    public async Task 認証先ではないURLは開かない(string url)
    {
        await Assert.That(AwsAuthenticationService.FindAuthorizationUri(url)).IsNull();
    }

    [Test]
    [Arguments("https://us-east-1.signin.amazonaws.com/authorize?state=example")]
    [Arguments("https://signin.aws.amazon.com/v1/authorize?state=example")]
    public async Task 認証URLはANSI色付きでも読み取れる(string url)
    {
        var found = AwsAuthenticationService.FindAuthorizationUri($"\u001b[32m{url}\u001b[0m");
        await Assert.That(found?.AbsoluteUri).IsEqualTo(url);
    }

    [Test]
    public async Task ブラウザ起動失敗はCLIをキャンセルして理由を返す()
    {
        var cli = new FakeCli { Lines = [AuthorizationUrl] };
        var error = await LoginService(cli, _ => throw new Win32Exception(2))
            .LoginAsync(Aws(), new RecordingProgress());
        await Assert.That(error?.Code).IsEqualTo("BrowserOpenFailed");
        await Assert.That(cli.CancellationObserved).IsTrue();
    }

    [Test]
    [Arguments("Invalid choice: 'login'", "AwsLoginUnsupported")]
    [Arguments("Profile test is already configured with SSO credentials.", "IncompatibleProfile")]
    [Arguments("Do you want to overwrite it? (y/n):", "InteractiveInputRequired")]
    [Arguments("Error with secret-token", "AwsLoginFailed")]
    public async Task CLIの失敗を分類し生の出力を漏らさない(string output, string code)
    {
        var error = await LoginService(new FakeCli { Lines = [output], ExitCode = 1 })
            .LoginAsync(Aws(), new RecordingProgress());
        await Assert.That(error?.Code).IsEqualTo(code);
        await Assert.That(error!.Message).DoesNotContain("secret-token");
    }

    [Test]
    public async Task CLI未導入を案内する()
    {
        var error = await LoginService(new FakeCli { Exception = new Win32Exception(2) })
            .LoginAsync(Aws(), new RecordingProgress());
        await Assert.That(error?.Code).IsEqualTo("AwsCliNotFound");
    }

    [Test]
    public async Task ユーザーのキャンセルを成功として扱わない()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await LoginService(new FakeCli())
            .LoginAsync(Aws(), new RecordingProgress(), cancellation.Token)).Throws<OperationCanceledException>();
    }

    [Test]
    [Arguments(true, AwsAuthenticationState.TemporaryCredentials)]
    [Arguments(false, AwsAuthenticationState.LongTermCredentials)]
    public async Task STSで確認した資格情報が一時キーか長期キーかを区別する(bool temporary, AwsAuthenticationState state)
    {
        using var dir = new TempDir();
        var path = await WriteCredentialsAsync(dir, temporary);
        var called = false;
        var service = new AwsAuthenticationService(new AwsClientFactory(profilesLocation: path), new FakeCli(),
            _ => throw new InvalidOperationException("確認ではブラウザを開かない"),
            (credentials, _) =>
            {
                called = true;
                return new StubSts();
            });

        var status = await service.GetStatusAsync(Aws());

        await Assert.That(called).IsTrue();
        await Assert.That(status.State).IsEqualTo(state);
        await Assert.That(status.Message).Contains("123456789012");
    }

    [Test]
    [Arguments("ExpiredToken", AwsAuthenticationState.NotAuthenticated)]
    [Arguments("InvalidClientTokenId", AwsAuthenticationState.NotAuthenticated)]
    [Arguments("ServiceUnavailable", AwsAuthenticationState.CheckFailed)]
    public async Task 失効と通信障害を区別する(string errorCode, AwsAuthenticationState state)
    {
        using var dir = new TempDir();
        var path = await WriteCredentialsAsync(dir, true);
        var service = new AwsAuthenticationService(new AwsClientFactory(profilesLocation: path), new FakeCli(), _ => { },
            (_, _) => new StubSts(new AmazonSecurityTokenServiceException("secret-detail") { ErrorCode = errorCode }));

        var status = await service.GetStatusAsync(Aws());

        await Assert.That(status.State).IsEqualTo(state);
        await Assert.That(status.Message).DoesNotContain("secret-detail");
    }

    [Test]
    public async Task プロファイルが無ければ未認証でSTSを呼ばない()
    {
        using var dir = new TempDir();
        var path = await WriteCredentialsAsync(dir, false);
        var service = new AwsAuthenticationService(new AwsClientFactory(profilesLocation: path), new FakeCli(), _ => { },
            (_, _) => throw new InvalidOperationException("呼び出してはいけない"));

        var status = await service.GetStatusAsync(Aws("missing"));

        await Assert.That(status.State).IsEqualTo(AwsAuthenticationState.NotAuthenticated);
    }

    [Test]
    public async Task SDKは作成後の資格情報ファイルの変更も読み込む()
    {
        using var dir = new TempDir();
        var path = await WriteCredentialsAsync(dir, false);
        var service = new AwsAuthenticationService(new AwsClientFactory(profilesLocation: path), new FakeCli(), _ => { },
            (_, _) => new StubSts());
        await service.GetStatusAsync(Aws());
        await WriteCredentialsAsync(dir, true);

        var status = await service.GetStatusAsync(Aws());

        await Assert.That(status.State).IsEqualTo(AwsAuthenticationState.TemporaryCredentials);
    }

    [Test]
    public async Task 新規ログイン用プロファイルにregionが無ければ接続先から補う()
    {
        using var dir = new TempDir();
        var path = dir.File("credentials");
        await File.WriteAllTextAsync(path, "[test]\nlogin_session = arn:aws:iam::000000000000:user/shionji-test\n");

        var result = new AwsClientFactory(profilesLocation: path).GetCredentials(Aws());

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsTypeOf<LoginAWSCredentials>();
        await Assert.That(((LoginAWSCredentials)result.Value).Options.Region).IsEqualTo("ap-northeast-1");
    }

    [Test]
    [NotInParallel]
    public async Task ログインのキャッシュが無ければブラウザを開かず未認証となる()
    {
        using var dir = new TempDir();
        var original = Environment.GetEnvironmentVariable("AWS_LOGIN_CACHE_DIRECTORY");
        Environment.SetEnvironmentVariable("AWS_LOGIN_CACHE_DIRECTORY", dir.Path);
        try
        {
            var path = dir.File("credentials");
            await File.WriteAllTextAsync(path, "[test]\nlogin_session = arn:aws:iam::000000000000:user/shionji-test\n");
            var service = new AwsAuthenticationService(new AwsClientFactory(profilesLocation: path), new FakeCli(),
                _ => throw new InvalidOperationException("ブラウザを開いてはいけない"),
                (_, _) => throw new InvalidOperationException("STS を呼んではいけない"));

            var status = await service.GetStatusAsync(Aws());

            await Assert.That(status.State).IsEqualTo(AwsAuthenticationState.NotAuthenticated);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_LOGIN_CACHE_DIRECTORY", original);
        }
    }

    private static async Task<string> WriteCredentialsAsync(TempDir dir, bool temporary)
    {
        var path = dir.File("credentials");
        await File.WriteAllTextAsync(path, "[test]\naws_access_key_id = test-key\naws_secret_access_key = test-secret\n"
            + (temporary ? "aws_session_token = test-token\n" : ""));
        return path;
    }

    private sealed class StubSts(Exception? error = null) : AmazonSecurityTokenServiceClient(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public override Task<GetCallerIdentityResponse> GetCallerIdentityAsync(
            GetCallerIdentityRequest request, CancellationToken cancellationToken = default) => error is null
            ? Task.FromResult(new GetCallerIdentityResponse { Account = "123456789012" })
            : Task.FromException<GetCallerIdentityResponse>(error);
    }

    private sealed class FakeCli : IAwsCliRunner
    {
        public string[] Lines { get; init; } = [];
        public int ExitCode { get; init; }
        public Exception? Exception { get; init; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public bool CancellationObserved { get; private set; }

        public Task<int> RunAsync(IReadOnlyList<string> arguments, Action<string> output, CancellationToken cancellationToken)
        {
            Arguments = arguments;
            if (Exception is not null)
                throw Exception;
            foreach (var line in Lines)
                output(line);
            CancellationObserved = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExitCode);
        }
    }

    private sealed class RecordingProgress : IProgress<AwsLoginProgress>
    {
        public List<AwsLoginProgress> Updates { get; } = [];
        public void Report(AwsLoginProgress value) => Updates.Add(value);
    }
}
