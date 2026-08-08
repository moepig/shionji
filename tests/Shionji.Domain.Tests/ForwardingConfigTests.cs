using Shionji.Domain.Configuration;
using Shionji.Domain.Primitives;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tests;

public class ForwardingConfigTests
{
    private static Destination StaticDest() =>
        new Destination.Static(TestData.Host("db.example.internal"), TestData.Port(5432));

    private static Destination QueryDest(ResourceQuery query, PortSelection? port = null) =>
        new Destination.Query(query, port ?? new PortSelection.Explicit(TestData.Port(5432)));

    private static Result<ForwardingConfig, ConfigValidationError> Create(Destination dest, GatewaySpec gateway) =>
        ForwardingConfig.Create(
            ConfigId.New(),
            TestData.Name("test"),
            TestData.Aws(),
            LocalPortSpec.Auto.Instance,
            dest,
            gateway,
            ConfigOptions.Default);

    // --- 経路 Direct が不正になる転送先 ---

    [Test]
    public async Task 直接指定の転送先に経路Directは不正()
    {
        var result = Create(StaticDest(), GatewaySpec.Direct.Instance);
        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Code).IsEqualTo(ConfigValidationError.Codes.GatewayRequired);
    }

    [Test]
    public async Task ElastiCache転送先に経路Directは不正()
    {
        var result = Create(QueryDest(TestData.QueryCache()), GatewaySpec.Direct.Instance);
        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Code).IsEqualTo(ConfigValidationError.Codes.GatewayRequired);
    }

    [Test]
    public async Task Aurora転送先に経路Directは不正()
    {
        var result = Create(QueryDest(TestData.QueryAurora()), GatewaySpec.Direct.Instance);
        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Code).IsEqualTo(ConfigValidationError.Codes.GatewayRequired);
    }

    // --- EC2 / ECS 転送先は Direct を許可 ---

    [Test]
    public async Task EC2転送先に経路Directは有効()
    {
        var result = Create(QueryDest(TestData.QueryEc2()), GatewaySpec.Direct.Instance);
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task ECS転送先に経路Directは有効()
    {
        var result = Create(QueryDest(TestData.QueryEcsTask()), GatewaySpec.Direct.Instance);
        await Assert.That(result.IsSuccess).IsTrue();
    }

    // --- 踏み台経由はすべての転送先で有効 ---

    [Test]
    public async Task 直接指定の転送先にEC2踏み台は有効()
    {
        await Assert.That(Create(StaticDest(), TestData.Ec2GatewayById()).IsSuccess).IsTrue();
        await Assert.That(Create(StaticDest(), TestData.Ec2GatewayByQuery()).IsSuccess).IsTrue();
    }

    [Test]
    public async Task 直接指定の転送先にECS踏み台は有効()
    {
        await Assert.That(Create(StaticDest(), TestData.EcsGateway()).IsSuccess).IsTrue();
    }

    [Test]
    public async Task クエリ転送先に踏み台は有効()
    {
        await Assert.That(Create(QueryDest(TestData.QueryCache()), TestData.Ec2GatewayById()).IsSuccess).IsTrue();
        await Assert.That(Create(QueryDest(TestData.QueryAurora()), TestData.EcsGateway()).IsSuccess).IsTrue();
        await Assert.That(Create(QueryDest(TestData.QueryEc2()), TestData.Ec2GatewayByQuery()).IsSuccess).IsTrue();
        await Assert.That(Create(QueryDest(TestData.QueryEcsTask()), TestData.Ec2GatewayById()).IsSuccess).IsTrue();
    }

    // --- FromResource の制約 ---

    [Test]
    public async Task ElastiCacheとAuroraは既定ポートを使える()
    {
        var cache = Create(QueryDest(TestData.QueryCache(), PortSelection.FromResource.Instance), TestData.Ec2GatewayById());
        var aurora = Create(QueryDest(TestData.QueryAurora(), PortSelection.FromResource.Instance), TestData.EcsGateway());
        await Assert.That(cache.IsSuccess).IsTrue();
        await Assert.That(aurora.IsSuccess).IsTrue();
    }

    [Test]
    public async Task EC2転送先に既定ポートは指定できない()
    {
        var result = Create(QueryDest(TestData.QueryEc2(), PortSelection.FromResource.Instance), TestData.Ec2GatewayById());
        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Code).IsEqualTo(ConfigValidationError.Codes.PortRequired);
    }

    [Test]
    public async Task ECS転送先に既定ポートは指定できない()
    {
        var result = Create(QueryDest(TestData.QueryEcsTask(), PortSelection.FromResource.Instance), GatewaySpec.Direct.Instance);
        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error.Code).IsEqualTo(ConfigValidationError.Codes.PortRequired);
    }
}
