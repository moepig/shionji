using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

public class SsoLoginTests
{
    private static ErrorDetail CredentialsError() => new(
        FailurePhase.Credentials, "SsoLoginRequired", "トークンが期限切れです。");

    /// <summary>ログインするまで資格情報エラーを返すカタログを仕込む。</summary>
    private static UiHarness ExpiredHarness(out Func<bool> loggedIn)
    {
        var ui = new UiHarness();
        var expired = true;
        ui.App.Catalog.Handler = (aws, query) => expired
            ? new ResolutionOutcome.Failed(CredentialsError())
            : FakeCatalog.DefaultHandler(aws, query);
        ui.SsoLogin.OnLogin = () => expired = false;
        loggedIn = () => !expired;
        return ui;
    }

    [Test]
    public async Task 資格情報エラーのときだけログインボタンが出る()
    {
        var ui = ExpiredHarness(out _);
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.CanSsoLogin).IsFalse();

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(detail.CanSsoLogin).IsTrue();
        await Assert.That(detail.Status).IsEqualTo(StatusKind.Failed);
    }

    [Test]
    public async Task ログイン成功で再解決され接続もやり直される()
    {
        var ui = ExpiredHarness(out _);
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await detail.SsoLoginCommand.ExecuteAsync(null);

        await Assert.That(ui.SsoLogin.Calls).IsEqualTo(1);
        await Assert.That(ui.App.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Established>();
        await Assert.That(detail.CanSsoLogin).IsFalse();
        await Assert.That(detail.ErrorText).IsNull();
    }

    [Test]
    public async Task 未接続で解決だけ失敗していた場合はログイン後に再解決のみ行う()
    {
        var ui = ExpiredHarness(out _);
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await detail.RefreshResolutionCommand.ExecuteAsync(null);
        await Assert.That(detail.CanSsoLogin).IsTrue();

        await detail.SsoLoginCommand.ExecuteAsync(null);

        await Assert.That(detail.CanSsoLogin).IsFalse();
        // 勝手に接続はしない
        await Assert.That(ui.App.Supervisor.GetState(config.Id)).IsTypeOf<SessionState.Idle>();
        await Assert.That(ui.App.Launcher.LaunchCount).IsEqualTo(0);
    }

    [Test]
    public async Task ログイン失敗はエラーとして表示される()
    {
        var ui = ExpiredHarness(out _);
        ui.SsoLogin.Result = new ErrorDetail(
            FailurePhase.Credentials, "LoginFailed", "承認がタイムアウトしました。");
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await detail.RefreshResolutionCommand.ExecuteAsync(null);

        await detail.SsoLoginCommand.ExecuteAsync(null);

        await Assert.That(detail.ErrorText!).Contains("承認がタイムアウト");
        await Assert.That(detail.CanSsoLogin).IsTrue();
    }
}
