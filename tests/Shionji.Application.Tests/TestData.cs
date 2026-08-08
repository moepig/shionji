using Shionji.Domain.Configuration;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Application.Tests;

internal static class TestData
{
    public static Port Port(int value) => Domain.ValueObjects.Port.Create(value).Value;

    public static HostName Host(string value) => HostName.Create(value).Value;

    public static AwsContext Aws() =>
        new(ProfileName.Create("dev").Value, AwsRegion.Create("ap-northeast-1").Value);

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

    public static ErrorDetail Error(FailurePhase phase = FailurePhase.Plugin) =>
        new(phase, "TestError", "テスト用のエラー");

    /// <summary>直接指定の転送先 + インスタンス ID 踏み台の設定 (解決不要で最短経路)。</summary>
    public static ForwardingConfig StaticConfig(
        bool autoReconnect = false,
        bool connectOnLaunch = false,
        int localPort = 15432,
        string name = "test") =>
        ForwardingConfig.Create(
            ConfigId.New(),
            ConfigName.Create(name).Value,
            Aws(),
            new LocalPortSpec.Fixed(Port(localPort)),
            new Destination.Static(Host("db.example.internal"), Port(5432)),
            new GatewaySpec.Ec2(new Ec2Selector.ById(InstanceId.Create("i-0123456789abcdef0").Value)),
            new ConfigOptions(autoReconnect, connectOnLaunch)).Value;

    /// <summary>ElastiCache クエリ転送先 + EC2 クエリ踏み台の設定 (両方の解決が必要)。</summary>
    public static ForwardingConfig QueryConfig(bool autoReconnect = false) =>
        ForwardingConfig.Create(
            ConfigId.New(),
            ConfigName.Create("query-test").Value,
            Aws(),
            LocalPortSpec.Auto.Instance,
            new Destination.Query(
                new ElastiCacheQuery(null, TagFilters.Empty, MatchPolicy.RequireSingle, CacheEndpointRole.Primary),
                PortSelection.FromResource.Instance),
            new GatewaySpec.Ec2(new Ec2Selector.ByQuery(new Ec2Query(null, TagFilters.Empty, MatchPolicy.RequireSingle))),
            new ConfigOptions(autoReconnect, ConnectOnLaunch: false)).Value;
}

internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("条件が時間内に満たされませんでした。");
            await Task.Delay(10);
        }
    }
}
