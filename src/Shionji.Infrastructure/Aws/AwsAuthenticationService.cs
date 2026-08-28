using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Amazon;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Aws;

public sealed partial class AwsAuthenticationService : IAwsAuthenticationService
{
    public const string BrowserHandledArgument = "--aws-login-browser-handled";
    private readonly AwsClientFactory _factory;
    private readonly IAwsCliRunner _cli;
    private readonly Action<Uri> _openBrowser;
    private readonly Func<AWSCredentials, AwsContext, IAmazonSecurityTokenService> _createSts;

    public AwsAuthenticationService(AwsClientFactory factory, string applicationPath)
        : this(factory,
            new AwsCliRunner($"\"{applicationPath.Replace('\\', '/')}\" {BrowserHandledArgument} %s"),
            uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }),
            (credentials, aws) => new AmazonSecurityTokenServiceClient(credentials,
                new AmazonSecurityTokenServiceConfig
                {
                    RegionEndpoint = RegionEndpoint.GetBySystemName(aws.Region.Value),
                    Timeout = TimeSpan.FromSeconds(20),
                    MaxErrorRetry = 0,
                }))
    {
    }

    internal AwsAuthenticationService(
        AwsClientFactory factory, IAwsCliRunner cli, Action<Uri> openBrowser,
        Func<AWSCredentials, AwsContext, IAmazonSecurityTokenService> createSts)
    {
        _factory = factory;
        _cli = cli;
        _openBrowser = openBrowser;
        _createSts = createSts;
    }

    public async Task<ErrorDetail?> LoginAsync(
        AwsContext aws, IProgress<AwsLoginProgress> progress, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var browserOpened = false;
        var needsInput = false;
        var incompatibleProfile = false;
        var unsupportedCommand = false;
        ErrorDetail? browserError = null;
        try
        {
            // 実プロセスの開始やブラウザの起動で UI スレッドを止めない。
            var exitCode = await Task.Run(() => _cli.RunAsync(
                ["login", "--profile", aws.Profile.Value, "--region", aws.Region.Value,
                    "--no-cli-pager", "--no-cli-auto-prompt", "--color", "off"],
                line =>
                {
                    needsInput |= line.Contains("(y/n)", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("EOF", StringComparison.OrdinalIgnoreCase);
                    incompatibleProfile |= line.Contains("already configured", StringComparison.OrdinalIgnoreCase)
                        && line.Contains("credentials", StringComparison.OrdinalIgnoreCase);
                    unsupportedCommand |= line.Contains("Invalid choice", StringComparison.OrdinalIgnoreCase);
                    if (browserOpened || FindAuthorizationUri(line) is not { } uri)
                        return;

                    browserOpened = true;
                    try
                    {
                        progress.Report(new(AwsLoginStage.OpeningBrowser));
                        _openBrowser(uri);
                        progress.Report(new(AwsLoginStage.WaitingForApproval));
                    }
                    catch (Exception)
                    {
                        browserError = Error("BrowserOpenFailed", "ブラウザを起動できませんでした。既定のブラウザを確認してください。");
                        timeout.Cancel();
                    }
                }, timeout.Token), timeout.Token);

            progress.Report(new(AwsLoginStage.ProcessExited, exitCode));

            if (needsInput)
                return Error("InteractiveInputRequired", "プロファイルの変更確認が必要です。ターミナルで aws login を実行してください。既存の認証設定は上書きしていません。");
            if (incompatibleProfile)
                return Error("IncompatibleProfile", "このプロファイルには別方式の資格情報が設定されています。SSO は既存の「SSO ログイン」を使用し、aws login には専用のプロファイルを指定してください。");
            if (unsupportedCommand)
                return Error("AwsLoginUnsupported", "aws login に対応した AWS CLI v2（2.32.0 以降）に更新してください。");
            return exitCode == 0 ? null : Error("AwsLoginFailed",
                $"aws login が終了コード {exitCode} で失敗しました。ブラウザでの承認とプロファイルの設定を確認してください。");
        }
        catch (OperationCanceledException) when (browserError is not null)
        {
            return browserError;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error("AwsLoginTimeout", "認証がタイムアウトしました。もう一度ログインしてください。");
        }
        catch (OperationCanceledException) { throw; }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return Error("AwsCliNotFound", "AWS CLI が見つかりません。AWS CLI v2（2.32.0 以降）をインストールして PATH を通し、Shionji を再起動してください。");
        }
        catch (Exception)
        {
            return Error("AwsLoginFailed", "aws login を実行できませんでした。AWS CLI のインストールと実行権限を確認してください。");
        }
    }

    public async Task<AwsAuthenticationStatus> GetStatusAsync(
        AwsContext aws, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            return await Task.Run(async () =>
            {
                var result = _factory.GetCredentials(aws);
                if (!result.IsSuccess)
                    return new AwsAuthenticationStatus(AwsAuthenticationState.NotAuthenticated, result.Error.Message);

                var credentials = result.Value;
                if (credentials is SSOAWSCredentials sso)
                    sso.Options.SupportsGettingNewToken = false;

                var snapshot = await credentials.GetCredentialsAsync().WaitAsync(timeout.Token);
                // トークンの存在だけでは期限切れ・失効を判断できないため STS で確認する。
                AWSCredentials fixedCredentials = snapshot.UseToken
                    ? new SessionAWSCredentials(snapshot.AccessKey, snapshot.SecretKey, snapshot.Token)
                    : new BasicAWSCredentials(snapshot.AccessKey, snapshot.SecretKey);
                using var sts = _createSts(fixedCredentials, aws);
                var identity = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), timeout.Token);
                return new AwsAuthenticationStatus(
                    snapshot.UseToken ? AwsAuthenticationState.TemporaryCredentials : AwsAuthenticationState.LongTermCredentials,
                    $"AWS アカウント: {identity.Account}");
            }, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var credentialError = AwsErrors.Classify(ex, FailurePhase.Permission, aws.Profile, isSsoProfile: false);
            return credentialError.Phase == FailurePhase.Credentials
                ? new AwsAuthenticationStatus(AwsAuthenticationState.NotAuthenticated,
                    "資格情報が無効か期限切れです。再ログインしてください。")
                : new AwsAuthenticationStatus(AwsAuthenticationState.CheckFailed,
                    "認証状態を確認できません。通信環境と AWS プロファイルを確認してください。");
        }
    }

    // 認証 URL 以外（ドキュメント、任意のホスト、別スキーム）は自動で開かない。
    internal static Uri? FindAuthorizationUri(string line)
    {
        foreach (Match match in UrlPattern().Matches(AnsiPattern().Replace(line, "")))
        {
            if (Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
                && SignInHostPattern().IsMatch(uri.Host)
                && uri.AbsolutePath is "/authorize" or "/v1/authorize")
                return uri;
        }
        return null;
    }

    private static ErrorDetail Error(string code, string message) => new(FailurePhase.Credentials, code, message);

    [GeneratedRegex(@"https://[^\s<>\""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"\x1b\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiPattern();

    [GeneratedRegex(@"^(?:[a-z0-9-]+\.)?signin\.(?:aws\.amazon\.com|amazonaws\.com(?:\.cn)?|amazonaws-us-gov\.com)$", RegexOptions.IgnoreCase)]
    private static partial Regex SignInHostPattern();
}
