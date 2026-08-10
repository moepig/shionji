using System.Text.Json.Serialization;

namespace Shionji.Infrastructure.Storage;

// ドメイン型を直接シリアライズせず、判別子付きの Storage DTO と相互変換する。
// プロパティは JSON 互換性のため素朴な型のみを使う。

public sealed class ConfigsDocument
{
    public int Version { get; set; } = 1;
    public List<ConfigDto> Configs { get; set; } = [];
}

public sealed class ConfigDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;

    /// <summary>null なら自動割当。</summary>
    public int? LocalPort { get; set; }

    public DestinationDto Destination { get; set; } = null!;
    public GatewayDto Gateway { get; set; } = null!;
    public bool AutoReconnect { get; set; }
    public bool ConnectOnLaunch { get; set; }

    /// <summary>接続中に実行できるコマンド。並びがそのままボタンの並びになる。</summary>
    public List<CommandDto> Commands { get; set; } = [];
}

public sealed class CommandDto
{
    public string Label { get; set; } = string.Empty;

    /// <summary>実行する内容。ローカル側のホストとポートのプレースホルダを含みうる。</summary>
    public string CommandLine { get; set; } = string.Empty;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StaticDestinationDto), "static")]
[JsonDerivedType(typeof(QueryDestinationDto), "query")]
public abstract class DestinationDto;

public sealed class StaticDestinationDto : DestinationDto
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}

public sealed class QueryDestinationDto : DestinationDto
{
    public QueryDto Query { get; set; } = null!;

    /// <summary>null ならリソースの既定ポートを使う。</summary>
    public int? Port { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ElastiCacheQueryDto), "elasticache")]
[JsonDerivedType(typeof(AuroraQueryDto), "aurora")]
[JsonDerivedType(typeof(Ec2QueryDto), "ec2")]
[JsonDerivedType(typeof(EcsTaskQueryDto), "ecsTask")]
public abstract class QueryDto
{
    public string? NamePattern { get; set; }
    public List<TagFilterDto> Tags { get; set; } = [];

    /// <summary>"RequireSingle" | "PickFirst"</summary>
    public string Match { get; set; } = "RequireSingle";
}

public sealed class TagFilterDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class ElastiCacheQueryDto : QueryDto
{
    /// <summary>"Primary" | "Reader" | "Configuration"</summary>
    public string Role { get; set; } = "Primary";
}

public sealed class AuroraQueryDto : QueryDto
{
    /// <summary>"Writer" | "Reader"</summary>
    public string Role { get; set; } = "Writer";
}

public sealed class Ec2QueryDto : QueryDto;

public sealed class EcsTaskQueryDto : QueryDto
{
    public string Cluster { get; set; } = string.Empty;
    public string? Service { get; set; }
    public string? Container { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DirectGatewayDto), "direct")]
[JsonDerivedType(typeof(Ec2GatewayDto), "ec2")]
[JsonDerivedType(typeof(EcsGatewayDto), "ecs")]
public abstract class GatewayDto;

public sealed class DirectGatewayDto : GatewayDto;

public sealed class Ec2GatewayDto : GatewayDto
{
    /// <summary>ID 直接指定の場合に設定。Query と排他。</summary>
    public string? InstanceId { get; set; }

    public Ec2QueryDto? Query { get; set; }
}

public sealed class EcsGatewayDto : GatewayDto
{
    public string Cluster { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string? Container { get; set; }
}
