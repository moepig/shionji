using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.TestSupport;
using Microsoft.Extensions.Logging;

namespace Shionji.Presentation.Tests;

public class AwsAuthenticationTests
{
    [Test]
    public async Task 起動時はログインせず既存の認証状態を確認する()
    {
        var ui = new UiHarness();
        ui.Authentication.Status = new(AwsAuthenticationState.TemporaryCredentials, "確認済み");

        await ui.Main.Authentication.StartAsync();

        await Assert.That(ui.Main.Authentication.IsAuthenticated).IsTrue();
        await Assert.That(ui.Main.Authentication.StatusText).IsEqualTo("一時認証済み");
        await Assert.That(ui.Authentication.Logins.Count).IsEqualTo(0);
        await Assert.That(ui.Authentication.Checks[0].Profile.Value).IsEqualTo("default");
    }

    [Test]
    [Arguments(AwsAuthenticationState.NotAuthenticated, "未認証・期限切れ")]
    [Arguments(AwsAuthenticationState.LongTermCredentials, "一時認証なし（長期キー使用中）")]
    [Arguments(AwsAuthenticationState.CheckFailed, "確認できません")]
    public async Task 再確認で認証失効や通信失敗を反映する(AwsAuthenticationState state, string text)
    {
        var ui = new UiHarness();
        ui.Authentication.Status = new(AwsAuthenticationState.TemporaryCredentials, "確認済み");
        await ui.Main.Authentication.StartAsync();
        ui.Authentication.Status = new(state, "次の結果");

        await ui.Main.Authentication.RefreshCommand.ExecuteAsync(null);

        await Assert.That(ui.Main.Authentication.IsAuthenticated).IsFalse();
        await Assert.That(ui.Main.Authentication.StatusText).IsEqualTo(text);
    }

    [Test]
    public async Task 選択した接続先のプロファイルとリージョンでログインする()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig());
        ui.Main.SelectedRow = ui.Main.Rows[0];
        ui.Authentication.Status = new(AwsAuthenticationState.TemporaryCredentials, "確認済み");

        await ui.Main.Authentication.LoginCommand.ExecuteAsync(null);

        await Assert.That(ui.Authentication.Logins.Single()).IsEqualTo(TestData.Aws());
        await Assert.That(ui.Main.Authentication.TargetText).IsEqualTo("dev / ap-northeast-1");
        await Assert.That(ui.Main.Authentication.IsAuthenticated).IsTrue();
        await Assert.That(ui.Authentication.Checks.Count).IsEqualTo(1);
    }

    [Test]
    public async Task 終了コードが成功でも確認できなければ認証済みにはしない()
    {
        var ui = new UiHarness();
        ui.Authentication.Status = new(AwsAuthenticationState.CheckFailed, "通信エラー");

        await ui.Main.Authentication.LoginCommand.ExecuteAsync(null);

        await Assert.That(ui.Main.Authentication.IsAuthenticated).IsFalse();
        await Assert.That(ui.Main.Authentication.StatusText).IsEqualTo("確認できません");
    }

    [Test]
    public async Task ログイン失敗の理由は再確認後も表示して操作を再開できる()
    {
        var ui = new UiHarness();
        ui.Authentication.LoginHandler = _ => Task.FromResult<ErrorDetail?>(
            new(FailurePhase.Credentials, "AwsCliNotFound", "AWS CLI が見つかりません。"));

        await ui.Main.Authentication.LoginCommand.ExecuteAsync(null);

        await Assert.That(ui.Main.Authentication.ErrorText).Contains("AWS CLI");
        await Assert.That(ui.Main.Authentication.LoginCommand.CanExecute(null)).IsTrue();
        await Assert.That(ui.Main.Activities.Any(a => a.Message.Contains("AWS CLI"))).IsTrue();
        await Assert.That(ui.Main.Authentication.IsAuthenticated).IsFalse();
    }

    [Test]
    public async Task ログイン中は多重実行せずキャンセル後に再確認する()
    {
        var ui = new UiHarness();
        ui.Authentication.LoginHandler = async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        };
        var login = ui.Main.Authentication.LoginCommand.ExecuteAsync(null);
        await Assert.That(ui.Main.Authentication.LoginCommand.CanExecute(null)).IsFalse();
        await Assert.That(ui.Main.Authentication.RefreshCommand.CanExecute(null)).IsFalse();
        await Assert.That(ui.Main.Authentication.DetailText).Contains("承認");

        ui.Main.Authentication.LoginCancelCommand.Execute(null);
        await login;

        await Assert.That(ui.Authentication.Logins.Count).IsEqualTo(1);
        await Assert.That(ui.Authentication.Checks.Count).IsEqualTo(1);
        await Assert.That(ui.Main.Authentication.IsBusy).IsFalse();
        await Assert.That(ui.Main.Authentication.LoginCommand.CanExecute(null)).IsTrue();
        await Assert.That(ui.App.WrittenWith("ログインをキャンセルしました").Level).IsEqualTo(LogLevel.Warning);
    }

    [Test]
    public async Task 遅れて届いた別プロファイルの結果で表示を上書きしない()
    {
        var ui = new UiHarness();
        var pending = new TaskCompletionSource<AwsAuthenticationStatus>();
        ui.Authentication.CheckHandler = _ => pending.Task;
        var first = ui.Main.Authentication.StartAsync();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig());
        ui.Authentication.CheckHandler = null;
        ui.Main.SelectedRow = ui.Main.Rows[0];

        pending.SetResult(new(AwsAuthenticationState.TemporaryCredentials, "古い結果"));
        await first;

        await Assert.That(ui.Main.Authentication.TargetText).Contains("dev");
        await Assert.That(ui.Main.Authentication.IsAuthenticated).IsFalse();
        await Assert.That(ui.Main.Authentication.DetailText).DoesNotContain("古い結果");
        await Assert.That(ui.App.Written.Any(log => log.Summary.Contains("一時認証済み"))).IsFalse();
    }

    [Test]
    public async Task 対象切替と終了でログインをキャンセルする()
    {
        var ui = new UiHarness();
        ui.Authentication.LoginHandler = async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        };
        var login = ui.Main.Authentication.LoginCommand.ExecuteAsync(null);
        await ui.App.Configs.SaveAsync(TestData.StaticConfig());
        ui.Main.SelectedRow = ui.Main.Rows[0];
        await login;
        await Assert.That(ui.Authentication.Checks.Last()).IsEqualTo(TestData.Aws());

        var second = ui.Main.Authentication.LoginCommand.ExecuteAsync(null);
        await ui.Main.Authentication.StopAsync();
        await second;
        await Assert.That(ui.Main.Authentication.LoginCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task ログが追加されても認証表示は変わらない()
    {
        var ui = new UiHarness();
        ui.Authentication.Status = new(AwsAuthenticationState.TemporaryCredentials, "確認済み");
        await ui.Main.Authentication.StartAsync();

        ui.App.Activity.Post(Application.ActivitySeverity.Error, "別の処理のエラー");

        await Assert.That(ui.Main.StatusText).IsEqualTo("別の処理のエラー");
        await Assert.That(ui.Main.Authentication.StatusText).IsEqualTo("一時認証済み");
    }

    [Test]
    public async Task 認証の進行と結果を画面とファイル用ロガーの両方に一度ずつ渡す()
    {
        var ui = new UiHarness();
        ui.Authentication.Status = new(AwsAuthenticationState.TemporaryCredentials, "確認済み");

        await ui.Main.Authentication.LoginCommand.ExecuteAsync(null);

        var written = ui.App.Written.Select(log => log.Summary).ToArray();
        await Assert.That(written.Length).IsEqualTo(4);
        await Assert.That(written[0]).Contains("aws login を開始");
        await Assert.That(written[1]).Contains("承認を待っています");
        await Assert.That(written[2]).Contains("aws login が完了");
        await Assert.That(written[3]).Contains("認証状態: 一時認証済み");
        await Assert.That(ui.App.Activity.Recent.Select(log => log.Message).SequenceEqual(written)).IsTrue();
    }

    [Test]
    public async Task 定期確認は状態が変わったときだけログに残す()
    {
        var ui = new UiHarness();
        await ui.Main.Authentication.StartAsync();
        await ui.Main.Authentication.RefreshCommand.ExecuteAsync(null);
        await Assert.That(ui.App.Written.Count).IsEqualTo(1);

        ui.Authentication.Status = new(AwsAuthenticationState.TemporaryCredentials, "再取得");
        await ui.Main.Authentication.RefreshCommand.ExecuteAsync(null);
        await ui.Main.Authentication.RefreshCommand.ExecuteAsync(null);

        await Assert.That(ui.App.Written.Count).IsEqualTo(2);
        await Assert.That(ui.App.Written.Last().Summary).Contains("一時認証済み");
    }

    [Test]
    [Arguments("AwsCliNotFound")]
    [Arguments("BrowserOpenFailed")]
    [Arguments("AwsLoginTimeout")]
    [Arguments("DO_NOT_LOG_UNKNOWN_CODE")]
    public async Task エラーや認証確認の自由文はログに渡さない(string code)
    {
        const string secret = "DO_NOT_LOG_TOKEN https://signin.aws.amazon.com/authorize?state=DO_NOT_LOG_STATE";
        var ui = new UiHarness();
        ui.Authentication.LoginHandler = _ => Task.FromResult<ErrorDetail?>(new(FailurePhase.Credentials, code, secret));
        ui.Authentication.Status = new(AwsAuthenticationState.CheckFailed, secret);

        await ui.Main.Authentication.LoginCommand.ExecuteAsync(null);

        await Assert.That(ui.App.Written.Any(log => log.Level == LogLevel.Error)).IsTrue();
        await Assert.That(string.Join("", ui.App.Written.Select(log => log.Summary))).DoesNotContain("DO_NOT_LOG");
        await Assert.That(string.Join("", ui.App.Activity.Recent.Select(log => log.Message))).DoesNotContain("DO_NOT_LOG");
    }

    [Test]
    public async Task 予期しない例外の内容をログに含めない()
    {
        var ui = new UiHarness();
        ui.Authentication.LoginHandler = _ => throw new InvalidOperationException("DO_NOT_LOG_SECRET_KEY");
        ui.Authentication.CheckHandler = _ => throw new InvalidOperationException("DO_NOT_LOG_SESSION_TOKEN");

        await ui.Main.Authentication.LoginCommand.ExecuteAsync(null);

        await Assert.That(ui.App.Written.Any(log => log.Level == LogLevel.Error)).IsTrue();
        await Assert.That(ui.App.Written.Any(log => log.Level == LogLevel.Warning)).IsTrue();
        await Assert.That(string.Join("", ui.App.Written.Select(log => log.Summary))).DoesNotContain("DO_NOT_LOG");
        await Assert.That(string.Join("", ui.App.Activity.Recent.Select(log => log.Message))).DoesNotContain("DO_NOT_LOG");
    }
}
