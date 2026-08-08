using Shionji.Domain.Primitives;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Configuration;

/// <summary>転送設定の検証エラー。</summary>
public sealed record ConfigValidationError(string Code, string Message)
{
    public static class Codes
    {
        /// <summary>転送先に対して踏み台の指定が必要。</summary>
        public const string GatewayRequired = "GatewayRequired";

        /// <summary>転送先ポートの明示指定が必要。</summary>
        public const string PortRequired = "PortRequired";
    }
}

/// <summary>
/// 名前付きのポートフォワード定義 (集約ルート)。
/// <see cref="Create"/> が転送先 × 経路の妥当性を検証する。
/// </summary>
public sealed record ForwardingConfig
{
    public ConfigId Id { get; }
    public ConfigName Name { get; }
    public AwsContext Aws { get; }
    public LocalPortSpec LocalPort { get; }
    public Destination Destination { get; }
    public GatewaySpec Gateway { get; }
    public ConfigOptions Options { get; }

    private ForwardingConfig(
        ConfigId id,
        ConfigName name,
        AwsContext aws,
        LocalPortSpec localPort,
        Destination destination,
        GatewaySpec gateway,
        ConfigOptions options)
    {
        Id = id;
        Name = name;
        Aws = aws;
        LocalPort = localPort;
        Destination = destination;
        Gateway = gateway;
        Options = options;
    }

    /// <summary>
    /// 不変条件:
    /// <list type="bullet">
    /// <item>Static / ElastiCache / Aurora 転送先に経路 Direct は指定できない (SSM セッションを張る相手にならないため)</item>
    /// <item>EC2 / ECS 転送先は既定ポートを持たないため、ポートの明示指定が必要</item>
    /// </list>
    /// </summary>
    public static Result<ForwardingConfig, ConfigValidationError> Create(
        ConfigId id,
        ConfigName name,
        AwsContext aws,
        LocalPortSpec localPort,
        Destination destination,
        GatewaySpec gateway,
        ConfigOptions options)
    {
        if (gateway is GatewaySpec.Direct)
        {
            switch (destination)
            {
                case Destination.Static:
                    return Invalid(
                        ConfigValidationError.Codes.GatewayRequired,
                        "エンドポイント直接指定の転送先には踏み台 (EC2 / ECS) の指定が必要です。");
                case Destination.Query { ResourceQuery: ElastiCacheQuery or AuroraQuery }:
                    return Invalid(
                        ConfigValidationError.Codes.GatewayRequired,
                        "ElastiCache / Aurora への転送には踏み台 (EC2 / ECS) の指定が必要です。");
            }
        }

        if (destination is Destination.Query
            {
                ResourceQuery: Ec2Query or EcsTaskQuery,
                Port: PortSelection.FromResource
            })
        {
            return Invalid(
                ConfigValidationError.Codes.PortRequired,
                "EC2 / ECS 転送先は既定ポートを持たないため、転送先ポートの明示指定が必要です。");
        }

        return Result<ForwardingConfig, ConfigValidationError>.Success(
            new ForwardingConfig(id, name, aws, localPort, destination, gateway, options));
    }

    private static Result<ForwardingConfig, ConfigValidationError> Invalid(string code, string message) =>
        Result<ForwardingConfig, ConfigValidationError>.Failure(new ConfigValidationError(code, message));
}
