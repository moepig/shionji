using Shionji.Domain.Configuration;
using Shionji.Domain.Ports;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Fakes;

/// <summary>デモモード用のインメモリ設定リポジトリ (実ファイルには触れない)。</summary>
public sealed class InMemoryConfigRepository(params ForwardingConfig[] seed) : IForwardingConfigRepository
{
    private readonly Dictionary<ConfigId, ForwardingConfig> _store =
        seed.ToDictionary(c => c.Id, c => c);

    public Task<IReadOnlyList<ForwardingConfig>> LoadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ForwardingConfig>>([.. _store.Values]);

    public Task SaveAsync(ForwardingConfig config, CancellationToken cancellationToken = default)
    {
        _store[config.Id] = config;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ConfigId id, CancellationToken cancellationToken = default)
    {
        _store.Remove(id);
        return Task.CompletedTask;
    }
}

/// <summary>デモモードの初期データ。フェイクの挙動キーワードを名前パターンに織り込んである。</summary>
public static class DemoData
{
    public static IReadOnlyList<ForwardingConfig> Configs() =>
    [
        // Aurora Writer を EC2 踏み台経由で。起動時自動接続
        Config(
            "api-db",
            LocalPort(13306),
            new Destination.Query(
                new AuroraQuery(Pattern("demo-aurora*"), TagFilters.Empty, MatchPolicy.RequireSingle, AuroraEndpointRole.Writer),
                PortSelection.FromResource.Instance),
            Ec2QueryGateway("demo-bastion*"),
            new ConfigOptions(AutoReconnect: true, ConnectOnLaunch: true)),

        // ElastiCache。確立後に疑似切断され自動再接続する (flaky)
        Config(
            "cache",
            LocalPort(16379),
            new Destination.Query(
                new ElastiCacheQuery(Pattern("demo-redis-flaky*"), TagFilters.Empty, MatchPolicy.RequireSingle, CacheEndpointRole.Primary),
                PortSelection.FromResource.Instance),
            Ec2QueryGateway("demo-bastion*"),
            new ConfigOptions(AutoReconnect: true, ConnectOnLaunch: false)),

        // EC2 インスタンスへの直接セッション (SSH 用)
        Config(
            "batch-ec2",
            LocalPort(12222),
            new Destination.Query(
                new Ec2Query(Pattern("demo-batch*"), TagFilters.Empty, MatchPolicy.RequireSingle),
                new PortSelection.Explicit(Port(22))),
            GatewaySpec.Direct.Instance,
            ConfigOptions.Default),

        // 複数一致エラーのデモ
        Config(
            "broken-ambiguous",
            LocalPort(15544),
            new Destination.Query(
                new Ec2Query(Pattern("ambiguous-*"), TagFilters.Empty, MatchPolicy.RequireSingle),
                new PortSelection.Explicit(Port(5432))),
            GatewaySpec.Direct.Instance,
            ConfigOptions.Default),

        // SSO トークン切れのデモ (プロファイル expired-sso)
        Config(
            "sso-expired",
            LocalPort(15433),
            new Destination.Static(Host("db.internal.example.com"), Port(5432)),
            new GatewaySpec.Ec2(new Ec2Selector.ById(InstanceId.Create("i-0123456789abcdef0").Value)),
            ConfigOptions.Default,
            profile: "expired-sso"),
    ];

    private static ForwardingConfig Config(
        string name,
        LocalPortSpec localPort,
        Destination destination,
        GatewaySpec gateway,
        ConfigOptions options,
        string profile = "demo") =>
        ForwardingConfig.Create(
            ConfigId.New(),
            ConfigName.Create(name).Value,
            new AwsContext(ProfileName.Create(profile).Value, AwsRegion.Create("ap-northeast-1").Value),
            localPort,
            destination,
            gateway,
            options).Value;

    private static GatewaySpec Ec2QueryGateway(string pattern) =>
        new GatewaySpec.Ec2(new Ec2Selector.ByQuery(
            new Ec2Query(Pattern(pattern), TagFilters.Empty, MatchPolicy.RequireSingle)));

    private static LocalPortSpec LocalPort(int port) => new LocalPortSpec.Fixed(Port(port));

    private static Port Port(int value) => Domain.ValueObjects.Port.Create(value).Value;

    private static HostName Host(string value) => HostName.Create(value).Value;

    private static NamePattern Pattern(string value) => NamePattern.Create(value).Value;
}
