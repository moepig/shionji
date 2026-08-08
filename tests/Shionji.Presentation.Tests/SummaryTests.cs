using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;
using Shionji.TestSupport;

namespace Shionji.Presentation.Tests;

/// <summary>一覧・詳細に出る要約文字列の組み立て。</summary>
public class SummaryTests
{
    [Test]
    public async Task 自動ポートは接続前はautoで接続後は実ポートを表示する()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.QueryConfig());
        var row = ui.Main.Rows[0];

        await Assert.That(row.Summary).StartsWith(":auto →");

        await row.ToggleConnectionCommand.ExecuteAsync(null);

        await Assert.That(row.Summary).StartsWith(":50000 →");
    }

    [Test]
    public async Task 検索前は未検索と表示される()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.QueryConfig());

        await Assert.That(ui.Main.Rows[0].Summary).Contains("未検索");
    }

    [Test]
    public async Task 解決できないときは理由が要約に出る()
    {
        var ui = new UiHarness();
        ui.App.Catalog.Handler = (_, _) => ResolutionOutcome.NotFound.Instance;
        var config = TestData.QueryConfig();
        await ui.App.Configs.SaveAsync(config);

        await ui.App.Resolution.RefreshAsync(config);

        await Assert.That(ui.Main.Rows[0].Summary).Contains("見つかりません");
    }

    [Test]
    public async Task EC2直接接続はdirect表記になる()
    {
        var ui = new UiHarness();
        var config = ForwardingConfig.Create(
            ConfigId.New(),
            ConfigName.Create("batch").Value,
            TestData.Aws(),
            new LocalPortSpec.Fixed(TestData.Port(12222)),
            new Destination.Query(
                new Ec2Query(NamePattern.Create("batch-*").Value, TagFilters.Empty, MatchPolicy.RequireSingle),
                new PortSelection.Explicit(TestData.Port(22))),
            GatewaySpec.Direct.Instance,
            ConfigOptions.Default).Value;
        await ui.App.Configs.SaveAsync(config);

        await ui.App.Resolution.RefreshAsync(config);

        await Assert.That(ui.Main.Rows[0].Summary).Contains("(direct)");
    }

    [Test]
    [Arguments("Direct", "直接")]
    [Arguments("Ec2ById", "i-0123456789abcdef0")]
    [Arguments("Ec2ByQuery", "検索")]
    [Arguments("Ecs", "prod-cluster/proxy")]
    public async Task 経路の要約が種別ごとに出る(string kind, string expected)
    {
        GatewaySpec gateway = kind switch
        {
            "Direct" => GatewaySpec.Direct.Instance,
            "Ec2ById" => new GatewaySpec.Ec2(new Ec2Selector.ById(InstanceId.Create("i-0123456789abcdef0").Value)),
            "Ec2ByQuery" => new GatewaySpec.Ec2(new Ec2Selector.ByQuery(
                new Ec2Query(NamePattern.Create("bastion-*").Value, TagFilters.Empty, MatchPolicy.RequireSingle))),
            _ => new GatewaySpec.Ecs(
                ClusterName.Create("prod-cluster").Value,
                ServiceName.Create("proxy").Value,
                null),
        };

        await Assert.That(ConfigDetailViewModel.GatewaySummary(gateway)).Contains(expected);
    }

    [Test]
    [Arguments(FailurePhase.Credentials, "認証")]
    [Arguments(FailurePhase.Permission, "権限")]
    [Arguments(FailurePhase.ResolveDestination, "転送先")]
    [Arguments(FailurePhase.ResolveGateway, "踏み台")]
    [Arguments(FailurePhase.StartSession, "セッション")]
    [Arguments(FailurePhase.Plugin, "plugin")]
    public async Task フェーズの表示名(FailurePhase phase, string expected)
    {
        await Assert.That(ConfigDetailViewModel.PhaseLabel(phase)).Contains(expected);
    }

    [Test]
    public async Task 全更新コマンドが全設定を解決する()
    {
        var ui = new UiHarness();
        await ui.App.Configs.SaveAsync(TestData.QueryConfig());
        await ui.App.Configs.SaveAsync(TestData.StaticConfig(name: "static-one"));

        await ui.Main.RefreshAllCommand.ExecuteAsync(null);

        // クエリ設定のみ 2 回 (転送先 + 踏み台) 呼ばれる
        await Assert.That(ui.App.Catalog.CallCount).IsEqualTo(2);
        await Assert.That(ui.App.Resolution.GetView(ui.Main.Rows[0].ConfigId)).IsNotNull();
        await Assert.That(ui.App.Resolution.GetView(ui.Main.Rows[1].ConfigId)).IsNotNull();
    }
}
