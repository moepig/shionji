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

        /// <summary>IAM 認証を利用できる転送先の指定が必要。</summary>
        public const string RdsIamDestinationRequired = "RdsIamDestinationRequired";
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

    /// <summary>接続中に実行できるコマンド。</summary>
    public LaunchCommands Commands { get; }

    /// <summary>RDS IAM 認証の DB ユーザー名。null は無効。パスワードやトークンは保存しない。</summary>
    public DatabaseUserName? RdsIamUserName { get; }

    private ForwardingConfig(
        ConfigId id,
        ConfigName name,
        AwsContext aws,
        LocalPortSpec localPort,
        Destination destination,
        GatewaySpec gateway,
        ConfigOptions options,
        LaunchCommands commands,
        DatabaseUserName? rdsIamUserName)
    {
        Id = id;
        Name = name;
        Aws = aws;
        LocalPort = localPort;
        Destination = destination;
        Gateway = gateway;
        Options = options;
        Commands = commands;
        RdsIamUserName = rdsIamUserName;
    }

    /// <summary>
    /// 不変条件:
    /// <list type="bullet">
    /// <item>Static / ElastiCache / Aurora 転送先に経路 Direct は指定できない (SSM セッションを張る相手にならないため)</item>
    /// <item>EC2 / ECS 転送先は既定ポートを持たないため、ポートの明示指定が必要</item>
    /// <item>RDS IAM 認証は Aurora 検索またはエンドポイント直接指定でのみ使用できる</item>
    /// </list>
    /// </summary>
    /// <param name="commands">接続中に実行できるコマンド。省略時は無し。</param>
    /// <param name="rdsIamUserName">RDS IAM 認証に使う DB ユーザー名。省略時は無効。</param>
    public static Result<ForwardingConfig, ConfigValidationError> Create(
        ConfigId id,
        ConfigName name,
        AwsContext aws,
        LocalPortSpec localPort,
        Destination destination,
        GatewaySpec gateway,
        ConfigOptions options,
        LaunchCommands? commands = null,
        DatabaseUserName? rdsIamUserName = null)
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

        if (rdsIamUserName is not null && destination is not
            (Destination.Static or Destination.Query { ResourceQuery: AuroraQuery }))
        {
            return Invalid(
                ConfigValidationError.Codes.RdsIamDestinationRequired,
                "RDS IAM 認証は Aurora 検索または RDS エンドポイントの直接指定で使用してください。");
        }

        return Result<ForwardingConfig, ConfigValidationError>.Success(
            new ForwardingConfig(
                id, name, aws, localPort, destination, gateway, options, commands ?? LaunchCommands.Empty,
                rdsIamUserName));
    }

    private static Result<ForwardingConfig, ConfigValidationError> Invalid(string code, string message) =>
        Result<ForwardingConfig, ConfigValidationError>.Failure(new ConfigValidationError(code, message));
}
