using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shionji.Domain.Configuration;
using Shionji.Domain.Primitives;
using Shionji.Domain.ValueObjects;

namespace Shionji.Presentation;

public enum DestinationKind
{
    Static,
    ElastiCache,
    Aurora,
    Ec2,
    EcsTask,
}

public enum GatewayKind
{
    Direct,
    Ec2ById,
    Ec2ByQuery,
    Ecs,
}

/// <summary>詳細ペイン内の編集モード。フォーム入力を検証しながら ForwardingConfig を組み立てる。</summary>
public sealed partial class ConfigEditorViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly ConfigId _id;

    public bool IsNew { get; }

    private ConfigEditorViewModel(MainViewModel owner, ConfigId id, bool isNew)
    {
        _owner = owner;
        _id = id;
        IsNew = isNew;
    }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Profile { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Region { get; set; } = "ap-northeast-1";

    /// <summary>空なら OS の自動割当。</summary>
    [ObservableProperty]
    public partial string LocalPortText { get; set; } = string.Empty;

    [NotifyPropertyChangedFor(nameof(IsStaticDestination))]
    [NotifyPropertyChangedFor(nameof(IsQueryDestination))]
    [NotifyPropertyChangedFor(nameof(IsNamedQueryDestination))]
    [NotifyPropertyChangedFor(nameof(IsCacheDestination))]
    [NotifyPropertyChangedFor(nameof(IsAuroraDestination))]
    [NotifyPropertyChangedFor(nameof(IsEcsTaskDestination))]
    [ObservableProperty]
    public partial DestinationKind DestinationKind { get; set; }

    public bool IsStaticDestination => DestinationKind == DestinationKind.Static;
    public bool IsQueryDestination => DestinationKind != DestinationKind.Static;
    public bool IsNamedQueryDestination => DestinationKind
        is DestinationKind.ElastiCache or DestinationKind.Aurora or DestinationKind.Ec2;
    public bool IsCacheDestination => DestinationKind == DestinationKind.ElastiCache;
    public bool IsAuroraDestination => DestinationKind == DestinationKind.Aurora;
    public bool IsEcsTaskDestination => DestinationKind == DestinationKind.EcsTask;

    [ObservableProperty]
    public partial string DestHost { get; set; } = string.Empty;

    /// <summary>ElastiCache / Aurora では空にするとリソースの既定ポートを使う。</summary>
    [ObservableProperty]
    public partial string DestPortText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DestNamePattern { get; set; } = string.Empty;

    /// <summary>「Key=v1|v2; Key2=v3」形式。</summary>
    [ObservableProperty]
    public partial string DestTagsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DestPickFirst { get; set; }

    [ObservableProperty]
    public partial CacheEndpointRole CacheRole { get; set; } = CacheEndpointRole.Primary;

    [ObservableProperty]
    public partial AuroraEndpointRole AuroraRole { get; set; } = AuroraEndpointRole.Writer;

    [ObservableProperty]
    public partial string DestCluster { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DestService { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DestContainer { get; set; } = string.Empty;

    [NotifyPropertyChangedFor(nameof(IsEc2ByIdGateway))]
    [NotifyPropertyChangedFor(nameof(IsEc2ByQueryGateway))]
    [NotifyPropertyChangedFor(nameof(IsEcsGateway))]
    [ObservableProperty]
    public partial GatewayKind GatewayKind { get; set; }

    public bool IsEc2ByIdGateway => GatewayKind == GatewayKind.Ec2ById;
    public bool IsEc2ByQueryGateway => GatewayKind == GatewayKind.Ec2ByQuery;
    public bool IsEcsGateway => GatewayKind == GatewayKind.Ecs;

    [ObservableProperty]
    public partial string GwInstanceId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GwNamePattern { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GwTagsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool GwPickFirst { get; set; }

    [ObservableProperty]
    public partial string GwCluster { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GwService { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GwContainer { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AutoReconnect { get; set; }

    [ObservableProperty]
    public partial bool ConnectOnLaunch { get; set; }

    [ObservableProperty]
    public partial string? ValidationError { get; set; }

    public static ConfigEditorViewModel ForNew(MainViewModel owner) =>
        new(owner, ConfigId.New(), isNew: true);

    public static ConfigEditorViewModel ForExisting(MainViewModel owner, ForwardingConfig config)
    {
        var editor = new ConfigEditorViewModel(owner, config.Id, isNew: false)
        {
            Name = config.Name.Value,
            Profile = config.Aws.Profile.Value,
            Region = config.Aws.Region.Value,
            LocalPortText = config.LocalPort is LocalPortSpec.Fixed fixedPort
                ? fixedPort.Port.Value.ToString()
                : string.Empty,
            AutoReconnect = config.Options.AutoReconnect,
            ConnectOnLaunch = config.Options.ConnectOnLaunch,
        };
        editor.PopulateDestination(config.Destination);
        editor.PopulateGateway(config.Gateway);
        return editor;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var built = Build();
        if (built.IsFailure)
        {
            ValidationError = built.Error;
            return;
        }

        ValidationError = null;
        await _owner.SaveConfigAsync(built.Value);
        _owner.CloseEditor(built.Value.Id);
    }

    [RelayCommand]
    private void Cancel() => _owner.CloseEditor(null);

    /// <summary>フォーム入力から検証済みの ForwardingConfig を組み立てる。</summary>
    public Result<ForwardingConfig, string> Build()
    {
        try
        {
            var name = Require(ConfigName.Create(Name));
            var aws = new AwsContext(
                Require(ProfileName.Create(Profile)),
                Require(AwsRegion.Create(Region)));
            var localPort = string.IsNullOrWhiteSpace(LocalPortText)
                ? (LocalPortSpec)LocalPortSpec.Auto.Instance
                : new LocalPortSpec.Fixed(ParsePort(LocalPortText, "ローカルポート"));

            var destination = BuildDestination();
            var gateway = BuildGateway();

            return ForwardingConfig.Create(
                    _id, name, aws, localPort, destination, gateway,
                    new ConfigOptions(AutoReconnect, ConnectOnLaunch))
                .Match(
                    Result<ForwardingConfig, string>.Success,
                    error => Result<ForwardingConfig, string>.Failure(error.Message));
        }
        catch (FormException ex)
        {
            return Result<ForwardingConfig, string>.Failure(ex.Message);
        }
    }

    private Destination BuildDestination()
    {
        if (DestinationKind == DestinationKind.Static)
        {
            return new Destination.Static(
                Require(HostName.Create(DestHost)),
                ParsePort(DestPortText, "転送先ポート"));
        }

        var name = string.IsNullOrWhiteSpace(DestNamePattern)
            ? null
            : Require(NamePattern.Create(DestNamePattern));
        var tags = ParseTags(DestTagsText);
        var match = DestPickFirst ? MatchPolicy.PickFirst : MatchPolicy.RequireSingle;

        ResourceQuery query = DestinationKind switch
        {
            DestinationKind.ElastiCache => new ElastiCacheQuery(name, tags, match, CacheRole),
            DestinationKind.Aurora => new AuroraQuery(name, tags, match, AuroraRole),
            DestinationKind.Ec2 => new Ec2Query(name, tags, match),
            DestinationKind.EcsTask => new EcsTaskQuery(
                Require(ClusterName.Create(DestCluster)),
                string.IsNullOrWhiteSpace(DestService) ? null : Require(ServiceName.Create(DestService)),
                string.IsNullOrWhiteSpace(DestContainer) ? null : Require(ContainerName.Create(DestContainer)),
                match),
            _ => throw new FormException("転送先の種別が不正です。"),
        };

        PortSelection port = string.IsNullOrWhiteSpace(DestPortText)
            ? PortSelection.FromResource.Instance
            : new PortSelection.Explicit(ParsePort(DestPortText, "転送先ポート"));

        return new Destination.Query(query, port);
    }

    private GatewaySpec BuildGateway() => GatewayKind switch
    {
        GatewayKind.Direct => GatewaySpec.Direct.Instance,
        GatewayKind.Ec2ById => new GatewaySpec.Ec2(new Ec2Selector.ById(
            Require(InstanceId.Create(GwInstanceId)))),
        GatewayKind.Ec2ByQuery => new GatewaySpec.Ec2(new Ec2Selector.ByQuery(new Ec2Query(
            string.IsNullOrWhiteSpace(GwNamePattern) ? null : Require(NamePattern.Create(GwNamePattern)),
            ParseTags(GwTagsText),
            GwPickFirst ? MatchPolicy.PickFirst : MatchPolicy.RequireSingle))),
        GatewayKind.Ecs => new GatewaySpec.Ecs(
            Require(ClusterName.Create(GwCluster)),
            Require(ServiceName.Create(GwService)),
            string.IsNullOrWhiteSpace(GwContainer) ? null : Require(ContainerName.Create(GwContainer))),
        _ => throw new FormException("経路の種別が不正です。"),
    };

    private void PopulateDestination(Destination destination)
    {
        switch (destination)
        {
            case Destination.Static s:
                DestinationKind = DestinationKind.Static;
                DestHost = s.Host.Value;
                DestPortText = s.Port.Value.ToString();
                break;

            case Destination.Query query:
                DestPortText = query.Port is PortSelection.Explicit explicitPort
                    ? explicitPort.Port.Value.ToString()
                    : string.Empty;
                DestNamePattern = query.ResourceQuery.Name?.Value ?? string.Empty;
                DestTagsText = FormatTags(query.ResourceQuery.Tags);
                DestPickFirst = query.ResourceQuery.Match == MatchPolicy.PickFirst;
                switch (query.ResourceQuery)
                {
                    case ElastiCacheQuery cache:
                        DestinationKind = DestinationKind.ElastiCache;
                        CacheRole = cache.Role;
                        break;
                    case AuroraQuery aurora:
                        DestinationKind = DestinationKind.Aurora;
                        AuroraRole = aurora.Role;
                        break;
                    case Ec2Query:
                        DestinationKind = DestinationKind.Ec2;
                        break;
                    case EcsTaskQuery ecs:
                        DestinationKind = DestinationKind.EcsTask;
                        DestCluster = ecs.Cluster.Value;
                        DestService = ecs.Service?.Value ?? string.Empty;
                        DestContainer = ecs.Container?.Value ?? string.Empty;
                        break;
                }

                break;
        }
    }

    private void PopulateGateway(GatewaySpec gateway)
    {
        switch (gateway)
        {
            case GatewaySpec.Direct:
                GatewayKind = GatewayKind.Direct;
                break;
            case GatewaySpec.Ec2 { Selector: Ec2Selector.ById byId }:
                GatewayKind = GatewayKind.Ec2ById;
                GwInstanceId = byId.Id.Value;
                break;
            case GatewaySpec.Ec2 { Selector: Ec2Selector.ByQuery byQuery }:
                GatewayKind = GatewayKind.Ec2ByQuery;
                GwNamePattern = byQuery.Query.Name?.Value ?? string.Empty;
                GwTagsText = FormatTags(byQuery.Query.Tags);
                GwPickFirst = byQuery.Query.Match == MatchPolicy.PickFirst;
                break;
            case GatewaySpec.Ecs ecs:
                GatewayKind = GatewayKind.Ecs;
                GwCluster = ecs.Cluster.Value;
                GwService = ecs.Service.Value;
                GwContainer = ecs.Container?.Value ?? string.Empty;
                break;
        }
    }

    private sealed class FormException(string message) : Exception(message);

    private static T Require<T>(Result<T, string> result) =>
        result.Match(v => v, error => throw new FormException(error));

    private static Port ParsePort(string text, string label)
    {
        if (!int.TryParse(text.Trim(), out var value))
            throw new FormException($"{label}には数値を指定してください: {text}");
        return Require(Port.Create(value));
    }

    /// <summary>「Key=v1|v2; Key2=v3」→ TagFilters。</summary>
    public static TagFilters ParseTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TagFilters.Empty;

        var filters = new List<TagFilter>();
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
                throw new FormException($"タグ条件は「キー=値」形式で指定してください: {entry}");

            var key = entry[..separator];
            var values = entry[(separator + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filters.Add(Require(TagFilter.Create(key, values)));
        }

        return TagFilters.From(filters);
    }

    public static string FormatTags(TagFilters tags) =>
        string.Join("; ", tags.Items.Select(f => $"{f.Key}={string.Join("|", f.Values)}"));
}
