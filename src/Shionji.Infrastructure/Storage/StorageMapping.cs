using Shionji.Domain.Configuration;
using Shionji.Domain.Primitives;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Storage;

/// <summary>Storage DTO とドメイン型の相互変換。</summary>
public static class StorageMapping
{
    private sealed class MappingException(string message) : Exception(message);

    private static T Require<T>(Result<T, string> result) =>
        result.Match(v => v, error => throw new MappingException(error));

    public static ConfigDto ToDto(ForwardingConfig config) => new()
    {
        Id = config.Id.Value,
        Name = config.Name.Value,
        Profile = config.Aws.Profile.Value,
        Region = config.Aws.Region.Value,
        LocalPort = config.LocalPort is LocalPortSpec.Fixed fixedPort ? fixedPort.Port.Value : null,
        Destination = ToDto(config.Destination),
        Gateway = ToDto(config.Gateway),
        AutoReconnect = config.Options.AutoReconnect,
        ConnectOnLaunch = config.Options.ConnectOnLaunch,
        Commands = [.. config.Commands.Items.Select(
            c => new CommandDto { Label = c.Label, CommandLine = c.CommandLine })],
    };

    public static Result<ForwardingConfig, string> ToDomain(ConfigDto dto)
    {
        try
        {
            var config = ForwardingConfig.Create(
                new ConfigId(dto.Id),
                Require(ConfigName.Create(dto.Name)),
                new AwsContext(
                    Require(ProfileName.Create(dto.Profile)),
                    Require(AwsRegion.Create(dto.Region))),
                dto.LocalPort is { } localPort
                    ? new LocalPortSpec.Fixed(Require(Port.Create(localPort)))
                    : LocalPortSpec.Auto.Instance,
                ToDomain(dto.Destination),
                ToDomain(dto.Gateway),
                new ConfigOptions(dto.AutoReconnect, dto.ConnectOnLaunch),
                LaunchCommands.From(
                    dto.Commands.Select(c => Require(LaunchCommand.Create(c.Label, c.CommandLine)))));
            return config.Match(
                Result<ForwardingConfig, string>.Success,
                error => Result<ForwardingConfig, string>.Failure(error.Message));
        }
        catch (MappingException ex)
        {
            return Result<ForwardingConfig, string>.Failure(ex.Message);
        }
    }

    private static DestinationDto ToDto(Destination destination) => destination switch
    {
        Destination.Static s => new StaticDestinationDto { Host = s.Host.Value, Port = s.Port.Value },
        Destination.Query q => new QueryDestinationDto
        {
            Query = ToDto(q.ResourceQuery),
            Port = q.Port is PortSelection.Explicit explicitPort ? explicitPort.Port.Value : null,
        },
        _ => throw new InvalidOperationException($"未知の転送先型: {destination.GetType()}"),
    };

    private static Destination ToDomain(DestinationDto dto) => dto switch
    {
        StaticDestinationDto s => new Destination.Static(
            Require(HostName.Create(s.Host)),
            Require(Port.Create(s.Port))),
        QueryDestinationDto q => new Destination.Query(
            ToDomain(q.Query),
            q.Port is { } port
                ? new PortSelection.Explicit(Require(Port.Create(port)))
                : PortSelection.FromResource.Instance),
        _ => throw new MappingException($"未知の転送先種別: {dto.GetType().Name}"),
    };

    private static QueryDto ToDto(ResourceQuery query)
    {
        QueryDto dto = query switch
        {
            ElastiCacheQuery c => new ElastiCacheQueryDto { Role = c.Role.ToString() },
            AuroraQuery a => new AuroraQueryDto { Role = a.Role.ToString() },
            Ec2Query => new Ec2QueryDto(),
            EcsTaskQuery e => new EcsTaskQueryDto
            {
                Cluster = e.Cluster.Value,
                Service = e.Service?.Value,
                Container = e.Container?.Value,
            },
            _ => throw new InvalidOperationException($"未知のクエリ型: {query.GetType()}"),
        };

        dto.NamePattern = query.Name?.Value;
        dto.Tags = [.. query.Tags.Items.Select(f => new TagFilterDto { Key = f.Key, Value = f.Value })];
        dto.Match = query.Match.ToString();
        return dto;
    }

    private static ResourceQuery ToDomain(QueryDto dto)
    {
        var name = dto.NamePattern is { Length: > 0 } pattern
            ? Require(NamePattern.Create(pattern))
            : null;
        var tags = TagFilters.From(
            dto.Tags.Select(t => Require(TagFilter.Create(t.Key, t.Value))));
        var match = ParseEnum<MatchPolicy>(dto.Match);

        return dto switch
        {
            ElastiCacheQueryDto c => new ElastiCacheQuery(name, tags, match, ParseEnum<CacheEndpointRole>(c.Role)),
            AuroraQueryDto a => new AuroraQuery(name, tags, match, ParseEnum<AuroraEndpointRole>(a.Role)),
            Ec2QueryDto => new Ec2Query(name, tags, match),
            EcsTaskQueryDto e => new EcsTaskQuery(
                Require(ClusterName.Create(e.Cluster)),
                e.Service is { Length: > 0 } service ? Require(ServiceName.Create(service)) : null,
                e.Container is { Length: > 0 } container ? Require(ContainerName.Create(container)) : null,
                match),
            _ => throw new MappingException($"未知のクエリ種別: {dto.GetType().Name}"),
        };
    }

    private static GatewayDto ToDto(GatewaySpec gateway) => gateway switch
    {
        GatewaySpec.Direct => new DirectGatewayDto(),
        GatewaySpec.Ec2 { Selector: Ec2Selector.ById byId } => new Ec2GatewayDto { InstanceId = byId.Id.Value },
        GatewaySpec.Ec2 { Selector: Ec2Selector.ByQuery byQuery } => new Ec2GatewayDto
        {
            Query = (Ec2QueryDto)ToDto(byQuery.Query),
        },
        GatewaySpec.Ecs ecs => new EcsGatewayDto
        {
            Cluster = ecs.Cluster.Value,
            Service = ecs.Service.Value,
            Container = ecs.Container?.Value,
        },
        _ => throw new InvalidOperationException($"未知の経路型: {gateway.GetType()}"),
    };

    private static GatewaySpec ToDomain(GatewayDto dto) => dto switch
    {
        DirectGatewayDto => GatewaySpec.Direct.Instance,
        Ec2GatewayDto { InstanceId: { Length: > 0 } instanceId } => new GatewaySpec.Ec2(
            new Ec2Selector.ById(Require(Domain.ValueObjects.InstanceId.Create(instanceId)))),
        Ec2GatewayDto { Query: { } query } => new GatewaySpec.Ec2(
            new Ec2Selector.ByQuery((Ec2Query)ToDomain(query))),
        Ec2GatewayDto => throw new MappingException("EC2 踏み台にはインスタンス ID か検索条件のどちらかが必要です。"),
        EcsGatewayDto ecs => new GatewaySpec.Ecs(
            Require(ClusterName.Create(ecs.Cluster)),
            Require(ServiceName.Create(ecs.Service)),
            ecs.Container is { Length: > 0 } container ? Require(ContainerName.Create(container)) : null),
        _ => throw new MappingException($"未知の経路種別: {dto.GetType().Name}"),
    };

    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new MappingException($"不正な値「{value}」({typeof(TEnum).Name})");
}
