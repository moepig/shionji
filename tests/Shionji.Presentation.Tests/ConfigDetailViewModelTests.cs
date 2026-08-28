using Microsoft.Extensions.Logging;
using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

public class ConfigDetailViewModelTests
{
    private static async Task AssertSecretNotLogged(UiHarness ui, string secret)
    {
        var written = ui.App.Written;
        var text = string.Join("\n", written.Select(log => log.Summary)
            .Concat(written.SelectMany(log => log.Details.Values))
            .Concat(written.Select(log => log.Exception?.ToString() ?? string.Empty))
            .Concat(ui.App.Activity.Recent.Select(entry => entry.Message)));
        await Assert.That(text.Contains(secret, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task 接続後に実際のDB接続先で毎回新しいトークンをコピーする()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(rdsIamUserName: "App_User"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.HasRdsIamAuthentication).IsTrue();
        await Assert.That(detail.CopyRdsIamPasswordCommand.CanExecute(null)).IsFalse();
        await detail.CopyRdsIamPasswordCommand.ExecuteAsync(null);
        await Assert.That(ui.RdsAuthTokens.Requests).IsEmpty();

        await detail.ConnectCommand.ExecuteAsync(null);
        await Assert.That(ui.RdsAuthTokens.Requests).IsEmpty();
        await Assert.That(ui.RdsAuthLogs).IsEmpty();
        await detail.CopyRdsIamPasswordCommand.ExecuteAsync(null);
        await Assert.That(ui.Clipboard.LastText).IsEqualTo("test-token-1");
        await detail.CopyRdsIamPasswordCommand.ExecuteAsync(null);
        await Assert.That(ui.Clipboard.LastText).IsEqualTo("test-token-2");
        await Assert.That(ui.Clipboard.SensitiveWrites).IsEqualTo(2);
        await Assert.That(ui.RdsAuthTokens.Requests[0]).IsEqualTo(
            (TestData.Aws(), "db.example.internal", 5432, "App_User"));
        await Assert.That(detail.CopyConfirmationText).Contains("15 分");

        var logs = ui.RdsAuthLogs;
        await Assert.That(string.Join(",", logs.Select(log => log.Detail("result"))))
            .IsEqualTo("Started,Succeeded,Started,Succeeded");
        foreach (var log in logs)
        {
            await Assert.That(log.Level).IsEqualTo(LogLevel.Information);
            await Assert.That(log.Detail("destination")).IsEqualTo("db.example.internal:5432");
            await Assert.That(log.Detail("profile")).IsEqualTo("dev@ap-northeast-1");
            await Assert.That(log.Detail("config")).IsEqualTo("test");
            await Assert.That(log.Detail("dbUser")).IsEqualTo("App_User");
        }
        await Assert.That(Guid.TryParse(logs[0].Detail("authAttempt"), out _)).IsTrue();
        await Assert.That(logs[0].Detail("authAttempt")).IsEqualTo(logs[1].Detail("authAttempt"));
        await Assert.That(logs[2].Detail("authAttempt")).IsEqualTo(logs[3].Detail("authAttempt"));
        await Assert.That(logs[0].Detail("authAttempt")).IsNotEqualTo(logs[2].Detail("authAttempt"));
        await Assert.That(ui.Main.StatusText).IsEqualTo(logs[3].Summary);
        await AssertSecretNotLogged(ui, "test-token");

        await detail.DisconnectCommand.ExecuteAsync(null);
        await Assert.That(detail.CopyRdsIamPasswordCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    [Arguments(AuroraEndpointRole.Writer, null)]
    [Arguments(AuroraEndpointRole.Reader, null)]
    [Arguments(AuroraEndpointRole.Reader, 3306)]
    public async Task Auroraの選択したエンドポイントと実際の転送先ポートを署名に使う(
        AuroraEndpointRole role, int? port)
    {
        var ui = new UiHarness();
        var host = role == AuroraEndpointRole.Reader ? "reader.rds.amazonaws.com" : "writer.rds.amazonaws.com";
        ui.App.Catalog.Handler = (_, query) => new ResolutionOutcome.Resolved(
            TestData.Resource(host: ((AuroraQuery)query).Role == role ? host : "wrong", defaultPort: 5432));
        await ui.App.Configs.SaveAsync(TestData.AuroraConfig(role, port));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await detail.ConnectCommand.ExecuteAsync(null);

        // 接続中に検索結果だけ更新されても、現在のトンネルが指す DB で署名する。
        ui.App.Catalog.Handler = (_, _) => new ResolutionOutcome.Resolved(
            TestData.Resource(host: "next.rds.amazonaws.com", defaultPort: 9999));
        await detail.RefreshResolutionCommand.ExecuteAsync(null);
        await detail.CopyRdsIamPasswordCommand.ExecuteAsync(null);

        await Assert.That(ui.RdsAuthTokens.Requests.Single()).IsEqualTo(
            (TestData.Aws(), host, port ?? 5432, "App_User"));
        await Assert.That(ui.RdsAuthLogs.Last().Detail("destination")).IsEqualTo($"{host}:{port ?? 5432}");
    }

    [Test]
    public async Task トークン取得中の再接続では古い結果をコピーしない()
    {
        var ui = new UiHarness();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.RdsAuthTokens.Gate = () => gate.Task;
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(rdsIamUserName: "app"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await detail.ConnectCommand.ExecuteAsync(null);
        var copy = detail.CopyRdsIamPasswordCommand.ExecuteAsync(null);
        // 取得中の時点で開始ログが見え、完了ログはまだ出ていない。
        await Assert.That(ui.RdsAuthLogs.Single().Detail("result")).IsEqualTo("Started");
        await detail.DisconnectCommand.ExecuteAsync(null);
        await detail.ConnectCommand.ExecuteAsync(null);
        gate.SetResult();
        await copy;

        await Assert.That(ui.Clipboard.LastText).IsNull();
        await Assert.That(detail.RdsIamError!).Contains("接続状態");
        await Assert.That(string.Join(",", ui.RdsAuthLogs.Select(log => log.Detail("result"))))
            .IsEqualTo("Started,Failed");
        await Assert.That(ui.RdsAuthLogs.Last().Detail("code")).IsEqualTo("ConnectionChanged");
        await AssertSecretNotLogged(ui, "test-token");
    }

    [Test]
    public async Task IAM認証の資格情報エラーからSSOログインして再生成できる()
    {
        var ui = new UiHarness();
        ui.RdsAuthTokens.Error = new ErrorDetail(FailurePhase.Credentials, "SsoLoginRequired", "SSO ログインが必要です。");
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(rdsIamUserName: "app"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await detail.ConnectCommand.ExecuteAsync(null);
        await detail.CopyRdsIamPasswordCommand.ExecuteAsync(null);
        await Assert.That(detail.CanSsoLogin).IsTrue();
        await Assert.That(ui.Clipboard.LastText).IsNull();

        ui.SsoLogin.OnLogin = () => ui.RdsAuthTokens.Error = null;
        await detail.SsoLoginCommand.ExecuteAsync(null);
        await detail.CopyRdsIamPasswordCommand.ExecuteAsync(null);
        await Assert.That(ui.Clipboard.LastText).IsEqualTo("test-token-2");
        await Assert.That(detail.RdsIamError).IsNull();
        await Assert.That(detail.CanSsoLogin).IsFalse();
    }

    [Test]
    public async Task トークン取得時の予期しない例外は秘密を表示しない()
    {
        var ui = new UiHarness();
        ui.RdsAuthTokens.Gate = () => throw new InvalidOperationException("secret-token");
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(rdsIamUserName: "app"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await detail.ConnectCommand.ExecuteAsync(null);
        await detail.CopyRdsIamPasswordCommand.ExecuteAsync(null);
        await Assert.That(detail.RdsIamError!.Contains("secret-token", StringComparison.Ordinal)).IsFalse();
        await Assert.That(ui.Clipboard.LastText).IsNull();
        var failed = ui.RdsAuthLogs.Last();
        await Assert.That(failed.Level).IsEqualTo(LogLevel.Error);
        await Assert.That(failed.Detail("code")).IsEqualTo("TokenGenerationFailed");
        await Assert.That(failed.Exception).IsNull();
        await AssertSecretNotLogged(ui, "secret-token");
    }

    [Test]
    [Arguments(FailurePhase.Credentials, "SsoLoginRequired")]
    [Arguments(FailurePhase.Permission, "AccessDenied")]
    [Arguments(FailurePhase.DatabaseAuthentication, "TokenGenerationFailed")]
    public async Task 取得失敗のログにはコードを残し理由本文は含めない(FailurePhase phase, string code)
    {
        var ui = new UiHarness();
        ui.RdsAuthTokens.Error = new ErrorDetail(phase, code, "取得失敗: secret-token");
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(rdsIamUserName: "app"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await detail.ConnectCommand.ExecuteAsync(null);
        await detail.CopyRdsIamPasswordCommand.ExecuteAsync(null);

        var logs = ui.RdsAuthLogs;
        await Assert.That(string.Join(",", logs.Select(log => log.Detail("result"))))
            .IsEqualTo("Started,Failed");
        var failed = logs[1];
        await Assert.That(failed.Level).IsEqualTo(LogLevel.Error);
        await Assert.That(failed.Detail("destination")).IsEqualTo("db.example.internal:5432");
        await Assert.That(failed.Detail("phase")).IsEqualTo(phase.ToString());
        await Assert.That(failed.Detail("code")).IsEqualTo(code);
        await Assert.That(failed.Detail("authAttempt")).IsEqualTo(logs[0].Detail("authAttempt"));
        await Assert.That(failed.Exception).IsNull();
        await Assert.That(ui.Main.StatusText).IsEqualTo(failed.Summary);
        await Assert.That(ui.Main.StatusSeverity).IsEqualTo(Shionji.Application.ActivitySeverity.Error);
        await AssertSecretNotLogged(ui, "secret-token");
    }

    [Test]
    public async Task 各項目が個別に取り出せる()
    {
        // 画面ではそれぞれにラベルを付けて並べるため、値が混ざっていないこと
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 13306));
        ui.Main.SelectedRow = ui.Main.Rows[0];

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.ProfileText).IsEqualTo("dev");
        await Assert.That(detail.RegionText).IsEqualTo("ap-northeast-1");
        await Assert.That(detail.GatewayText).IsEqualTo("EC2 踏み台 i-0123456789abcdef0");
        await Assert.That(detail.DestinationText).IsEqualTo("db.example.internal:5432");
        await Assert.That(detail.LocalPortText).IsEqualTo("localhost:13306");
        await Assert.That(detail.SessionText).IsEqualTo("未接続");
    }

    [Test]
    public async Task 自動割当は接続前後で表示が変わる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.QueryConfig());
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await Assert.That(detail.LocalPortText).IsEqualTo("自動割当 (接続時に決定)");
        await Assert.That(detail.LocalEndpoint).IsNull();

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(detail.LocalPortText).IsEqualTo("localhost:50000");
        await Assert.That(detail.LocalEndpoint).IsEqualTo("localhost:50000");
    }

    [Test]
    public async Task 候補とログの節は中身があるときだけ出す()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await Assert.That(detail.HasCandidates).IsFalse();
        await Assert.That(detail.HasLog).IsFalse();

        await row.ToggleConnectionCommand.ExecuteAsync(null);
        ui.App.Launcher.LastHandle.EmitLog("Port opened.");

        await Assert.That(detail.HasLog).IsTrue();
    }

    [Test]
    public async Task 確立中はローカルエンドポイントをコピーできる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 13306));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.LocalEndpoint).IsEqualTo("localhost:13306");

        detail.CopyLocalEndpointCommand.Execute(null);
        await Assert.That(ui.Clipboard.LastText).IsEqualTo("localhost:13306");
    }

    [Test]
    public async Task 確立時刻は現地時刻で表示される()
    {
        // 履歴一覧が現地時刻なので、詳細も揃っていないと同じ出来事が別の時刻に見える
        var ui = new UiHarness();
        ui.App.Clock.UtcNow = new DateTimeOffset(2026, 8, 8, 2, 13, 56, TimeSpan.Zero);
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        var expected = ui.App.Clock.UtcNow.ToLocalTime().ToString("HH:mm:ss");
        await Assert.That(detail.SessionText).IsEqualTo($"確立 ({expected} から)");
    }

    [Test]
    public async Task 接続と切断は状態に応じて片方だけ押せる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        // 未接続: 接続できる / 切断できない
        await Assert.That(detail.CanConnect).IsTrue();
        await Assert.That(detail.CanDisconnect).IsFalse();

        await detail.ConnectCommand.ExecuteAsync(null);

        // 確立: 接続できない / 切断できる
        await Assert.That(detail.CanConnect).IsFalse();
        await Assert.That(detail.CanDisconnect).IsTrue();

        await detail.DisconnectCommand.ExecuteAsync(null);

        await Assert.That(detail.CanConnect).IsTrue();
        await Assert.That(detail.CanDisconnect).IsFalse();
    }

    [Test]
    public async Task 失敗状態からは接続し直せる()
    {
        var ui = new UiHarness();
        ui.App.Probe.BusyPorts.Add(15432);
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await detail.ConnectCommand.ExecuteAsync(null);

        await Assert.That(detail.Status).IsEqualTo(StatusKind.Failed);
        await Assert.That(detail.CanConnect).IsTrue();
        await Assert.That(detail.CanDisconnect).IsFalse();
    }

    [Test]
    public async Task コピーすると完了表示が出る()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 13306));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await Assert.That(detail.IsCopyConfirmationVisible).IsFalse();

        detail.CopyLocalEndpointCommand.Execute(null);

        await Assert.That(detail.IsCopyConfirmationVisible).IsTrue();
        await Assert.That(detail.CopyConfirmationText).IsEqualTo("接続先をコピーしました");
    }

    [Test]
    public async Task セッションログは選択してコピーできるテキストになる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);
        ui.App.Launcher.LastHandle.EmitLog("first line");
        ui.App.Launcher.LastHandle.EmitLog("second line", isError: true);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        // 画面では読み取り専用テキストとして出すので、全文が 1 つの文字列になっている
        await Assert.That(detail.LogText)
            .IsEqualTo($"first line{Environment.NewLine}[stderr] second line");
    }

    [Test]
    public async Task ログが無ければ本文も空()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        ui.Main.SelectedRow = ui.Main.Rows[0];
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;

        await Assert.That(detail.LogText).IsEmpty();
        await Assert.That(detail.HasLog).IsFalse();
    }

    [Test]
    public async Task 転送先を特定できないと一覧と詳細で赤くする()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (_, _) => new ResolutionOutcome.Ambiguous(
            [TestData.Resource("a"), TestData.Resource("b"), TestData.Resource("c")]);
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        ui.Main.SelectedRow = ui.Main.Rows[0];

        await ui.App.Resolution.RefreshAsync(config);

        var row = ui.Main.Rows[0];
        await Assert.That(row.DestinationText).IsEqualTo("複数一致 (3 件)");
        await Assert.That(row.DestinationHasError).IsTrue();

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.DestinationHasError).IsTrue();
    }

    [Test]
    public async Task 特定できていれば赤くしない()
    {
        var ui = new UiHarness();
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        ui.Main.SelectedRow = ui.Main.Rows[0];

        await ui.App.Resolution.RefreshAsync(config);

        await Assert.That(ui.Main.Rows[0].DestinationHasError).IsFalse();
        await Assert.That(((ConfigDetailViewModel)ui.Main.DetailContent!).DestinationHasError).IsFalse();
    }

    [Test]
    public async Task 失敗時はフェーズ付きのエラーが表示される()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (_, _) => ResolutionOutcome.NotFound.Instance;
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.Status).IsEqualTo(StatusKind.Failed);
        await Assert.That(detail.ErrorText!).Contains("転送先の自動検索");
    }

    [Test]
    public async Task 複数一致の候補が一覧表示される()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (aws, query) => query is Shionji.Domain.Configuration.ElastiCacheQuery
            ? new ResolutionOutcome.Ambiguous(
            [
                TestData.Resource("redis-a", host: "a.cache.example"),
                TestData.Resource("redis-b", host: "b.cache.example"),
            ])
            : FakeCatalog.DefaultHandler(aws, query);
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);
        ui.Main.SelectedRow = ui.Main.Rows[0];

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await detail.RefreshResolutionCommand.ExecuteAsync(null);

        await Assert.That(detail.Candidates.Count).IsEqualTo(2);
        await Assert.That(detail.Candidates[0]).Contains("redis-a");
        await Assert.That(detail.ErrorText!).Contains("2 件");
    }

    [Test]
    public async Task 選択前のログも行切替後も失われない()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db", localPort: 15001));
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "cache", localPort: 15002));
        var apiRow = ui.Main.Rows.Single(r => r.Name == "api-db");
        var cacheRow = ui.Main.Rows.Single(r => r.Name == "cache");

        // どの行も選択していない状態でログが発生する
        await apiRow.ToggleConnectionCommand.ExecuteAsync(null);
        ui.App.Launcher.LastHandle.EmitLog("Port 15001 opened for sessionId x.");

        ui.Main.SelectedRow = apiRow;
        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.LogLines.Count).IsEqualTo(1);

        // 別の行へ切り替えて戻ってもログが残る
        ui.Main.SelectedRow = cacheRow;
        ui.Main.SelectedRow = apiRow;
        detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.LogLines.Count).IsEqualTo(1);
    }

    [Test]
    public async Task セッションログが詳細に流れる()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "api-db"));
        var row = ui.Main.Rows[0];
        ui.Main.SelectedRow = row;
        await row.ToggleConnectionCommand.ExecuteAsync(null);

        ui.App.Launcher.LastHandle.EmitLog("Port 15432 opened for sessionId x.");

        var detail = (ConfigDetailViewModel)ui.Main.DetailContent!;
        await Assert.That(detail.LogLines.Count).IsEqualTo(1);
    }
}
