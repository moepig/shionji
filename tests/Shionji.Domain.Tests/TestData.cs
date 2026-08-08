using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tests;

/// <summary>検証済み VO を簡潔に組み立てるテスト用ファクトリ。</summary>
internal static class TestData
{
    public static Port Port(int value) => ValueObjects.Port.Create(value).Value;

    public static HostName Host(string value) => HostName.Create(value).Value;

    public static AwsContext Aws() =>
        new(ProfileName.Create("dev").Value, AwsRegion.Create("ap-northeast-1").Value);

    public static ConfigName Name(string value) => ConfigName.Create(value).Value;

    public static InstanceId Instance(string value = "i-0123456789abcdef0") =>
        InstanceId.Create(value).Value;

    public static ClusterName Cluster(string value = "app-cluster") =>
        ClusterName.Create(value).Value;

    public static ServiceName Service(string value = "api") =>
        ServiceName.Create(value).Value;

    public static NamePattern Pattern(string value) => NamePattern.Create(value).Value;

    public static ElastiCacheQuery QueryCache() =>
        new(null, TagFilters.Empty, MatchPolicy.RequireSingle, CacheEndpointRole.Primary);

    public static AuroraQuery QueryAurora() =>
        new(null, TagFilters.Empty, MatchPolicy.RequireSingle, AuroraEndpointRole.Writer);

    public static Ec2Query QueryEc2() =>
        new(null, TagFilters.Empty, MatchPolicy.RequireSingle);

    public static EcsTaskQuery QueryEcsTask() =>
        new(Cluster(), Service(), null, MatchPolicy.RequireSingle);

    public static GatewaySpec Ec2GatewayById(string id = "i-0123456789abcdef0") =>
        new GatewaySpec.Ec2(new Ec2Selector.ById(Instance(id)));

    public static GatewaySpec Ec2GatewayByQuery() =>
        new GatewaySpec.Ec2(new Ec2Selector.ByQuery(QueryEc2()));

    public static GatewaySpec EcsGateway() =>
        new GatewaySpec.Ecs(Cluster(), Service(), null);

    public static ResolvedResource Resource(
        string id = "resource-1",
        string? host = "10.0.0.1",
        int? defaultPort = null,
        string? ssmTarget = null) =>
        new(
            new ResourceId(id),
            id,
            host is null ? null : Host(host),
            defaultPort is null ? null : Port(defaultPort.Value),
            ssmTarget is null ? null : new SsmTargetId(ssmTarget),
            DateTimeOffset.UnixEpoch);

    public static ForwardingConfig Config(
        Destination destination,
        GatewaySpec gateway,
        ConfigOptions? options = null) =>
        ForwardingConfig.Create(
            ConfigId.New(),
            Name("test"),
            Aws(),
            new LocalPortSpec.Fixed(Port(15432)),
            destination,
            gateway,
            options ?? ConfigOptions.Default).Value;

    public static TunnelPlan Plan() =>
        new(
            Aws(),
            new SsmTargetId("i-0123456789abcdef0"),
            new SessionMode.DirectForward(Port(22)),
            Port(12222));

    public static ErrorDetail Error(FailurePhase phase = FailurePhase.Plugin) =>
        new(phase, "TestError", "テスト用のエラー");
}
