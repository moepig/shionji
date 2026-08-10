using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shionji.Domain.Configuration;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
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

    /// <summary>編集が終わった (保存 / キャンセル)。別ウィンドウで開いている場合はこれで閉じる。</summary>
    public event EventHandler? Closed;

    public string WindowTitle => IsNew ? "接続先設定の追加" : "接続先設定の編集";

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
    [NotifyPropertyChangedFor(nameof(CanTestSearch))]
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

    /// <summary>転送先のタグ条件。行を並べるとすべて満たすもの (AND) を探す。</summary>
    public ObservableCollection<TagEntryViewModel> DestTags { get; } = [];

    [RelayCommand]
    private void AddDestTag() => DestTags.Add(new TagEntryViewModel(e => DestTags.Remove(e)));

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
    [NotifyPropertyChangedFor(nameof(CanTestSearch))]
    [ObservableProperty]
    public partial GatewayKind GatewayKind { get; set; }

    public bool IsEc2ByIdGateway => GatewayKind == GatewayKind.Ec2ById;
    public bool IsEc2ByQueryGateway => GatewayKind == GatewayKind.Ec2ByQuery;
    public bool IsEcsGateway => GatewayKind == GatewayKind.Ecs;

    [ObservableProperty]
    public partial string GwInstanceId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GwNamePattern { get; set; } = string.Empty;

    /// <summary>踏み台のタグ条件 (AND)。</summary>
    public ObservableCollection<TagEntryViewModel> GwTags { get; } = [];

    [RelayCommand]
    private void AddGwTag() => GwTags.Add(new TagEntryViewModel(e => GwTags.Remove(e)));

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

    // --- 接続中に実行できるコマンド ---

    /// <summary>コマンドの入力行。並びがそのまま詳細ペインのボタンの並びになる。</summary>
    public ObservableCollection<CommandEntryViewModel> Commands { get; } = [];

    /// <summary>コマンド行に書けるプレースホルダの案内。</summary>
    public string CommandPlaceholderHint =>
        $"{CommandTemplate.HostPlaceholder} と {CommandTemplate.PortPlaceholder} は、"
        + "実行時に待ち受けているローカル側のホストとポート番号に置き換わります。";

    [RelayCommand]
    private void AddCommand() => Commands.Add(new CommandEntryViewModel(e => Commands.Remove(e)));

    [ObservableProperty]
    public partial string? ValidationError { get; set; }

    // --- 入力した条件での検索テスト ---

    /// <summary>検索テストの結果 (見つかったリソース、または理由)。</summary>
    [ObservableProperty]
    public partial string? SearchTestResult { get; set; }

    [ObservableProperty]
    public partial bool SearchTestFailed { get; set; }

    [ObservableProperty]
    public partial bool IsTestingSearch { get; set; }

    /// <summary>複数一致したときの候補。</summary>
    public ObservableCollection<string> SearchTestCandidates { get; } = [];

    /// <summary>検索テストを出す価値があるか (検索条件を使う設定のときだけ)。</summary>
    public bool CanTestSearch => IsQueryDestination || IsEc2ByQueryGateway || IsEcsGateway;

    /// <summary>設定を保存する前に、入力した条件で実際に検索してみる。</summary>
    [RelayCommand]
    private async Task TestSearchAsync()
    {
        SearchTestCandidates.Clear();
        SearchTestResult = null;
        SearchTestFailed = false;

        AwsContext aws;
        try
        {
            aws = new AwsContext(
                Require(ProfileName.Create(Profile)),
                Require(AwsRegion.Create(Region)));
        }
        catch (FormException ex)
        {
            SearchTestFailed = true;
            SearchTestResult = ex.Message;
            return;
        }

        IsTestingSearch = true;
        try
        {
            var lines = new List<string>();
            var failed = false;

            foreach (var (label, query) in CollectQueriesToTest())
            {
                if (query is null)
                    continue;

                var outcome = await _owner.TestSearchAsync(aws, query);
                lines.Add($"{label}: {DescribeOutcome(outcome)}");
                if (outcome is not ResolutionOutcome.Resolved)
                    failed = true;
                if (outcome is ResolutionOutcome.Ambiguous ambiguous)
                {
                    foreach (var candidate in ambiguous.Candidates)
                        SearchTestCandidates.Add(DescribeCandidate(candidate));
                }
            }

            SearchTestFailed = failed;
            SearchTestResult = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "検索する条件がありません。";
        }
        finally
        {
            IsTestingSearch = false;
        }
    }

    /// <summary>いま入力されている条件のうち、検索できるものを取り出す。</summary>
    private IEnumerable<(string Label, ResourceQuery? Query)> CollectQueriesToTest()
    {
        if (IsQueryDestination)
            yield return ("転送先", TryBuild(BuildDestinationQuery));
        if (IsEc2ByQueryGateway || IsEcsGateway)
            yield return ("踏み台", TryBuild(BuildGatewayQuery));
    }

    private ResourceQuery? TryBuild(Func<ResourceQuery> build)
    {
        try
        {
            return build();
        }
        catch (FormException ex)
        {
            SearchTestFailed = true;
            SearchTestResult = ex.Message;
            return null;
        }
    }

    private static string DescribeOutcome(ResolutionOutcome outcome) => outcome switch
    {
        ResolutionOutcome.Resolved resolved =>
            $"{resolved.Resource.DisplayName} が見つかりました" +
            (resolved.Resource.Host is { } host ? $" ({host.Value})" : string.Empty),
        ResolutionOutcome.NotFound => "条件に一致するリソースがありません",
        ResolutionOutcome.Ambiguous ambiguous => $"{ambiguous.Candidates.Count} 件が一致しました。条件を絞り込んでください",
        ResolutionOutcome.Failed failed => $"検索に失敗しました - {failed.Error.Message}",
        _ => "不明な結果",
    };

    private static string DescribeCandidate(ResolvedResource resource) =>
        resource.Host is { } host
            ? $"{resource.DisplayName} ({host.Value})"
            : resource.DisplayName;

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
        foreach (var command in config.Commands.Items)
        {
            editor.Commands.Add(new CommandEntryViewModel(e => editor.Commands.Remove(e))
            {
                Label = command.Label,
                CommandLine = command.CommandLine,
            });
        }

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
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        _owner.CloseEditor(null);
        Closed?.Invoke(this, EventArgs.Empty);
    }

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
                    new ConfigOptions(AutoReconnect, ConnectOnLaunch),
                    BuildCommands())
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

        var query = BuildDestinationQuery();

        PortSelection port = string.IsNullOrWhiteSpace(DestPortText)
            ? PortSelection.FromResource.Instance
            : new PortSelection.Explicit(ParsePort(DestPortText, "転送先ポート"));

        return new Destination.Query(query, port);
    }

    /// <summary>転送先の検索条件。検索テストと保存の両方から使う。</summary>
    private ResourceQuery BuildDestinationQuery()
    {
        var name = string.IsNullOrWhiteSpace(DestNamePattern)
            ? null
            : Require(NamePattern.Create(DestNamePattern));
        var tags = BuildTags(DestTags, "転送先");
        var match = DestPickFirst ? MatchPolicy.PickFirst : MatchPolicy.RequireSingle;

        return DestinationKind switch
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
    }

    /// <summary>踏み台の検索条件。検索を伴わない経路では例外になる。</summary>
    private ResourceQuery BuildGatewayQuery() => GatewayKind switch
    {
        GatewayKind.Ec2ByQuery => new Ec2Query(
            string.IsNullOrWhiteSpace(GwNamePattern) ? null : Require(NamePattern.Create(GwNamePattern)),
            BuildTags(GwTags, "踏み台"),
            GwPickFirst ? MatchPolicy.PickFirst : MatchPolicy.RequireSingle),
        GatewayKind.Ecs => new EcsTaskQuery(
            Require(ClusterName.Create(GwCluster)),
            Require(ServiceName.Create(GwService)),
            string.IsNullOrWhiteSpace(GwContainer) ? null : Require(ContainerName.Create(GwContainer)),
            MatchPolicy.RequireSingle),
        _ => throw new FormException("この経路は検索条件を使いません。"),
    };

    private GatewaySpec BuildGateway() => GatewayKind switch
    {
        GatewayKind.Direct => GatewaySpec.Direct.Instance,
        GatewayKind.Ec2ById => new GatewaySpec.Ec2(new Ec2Selector.ById(
            Require(InstanceId.Create(GwInstanceId)))),
        GatewayKind.Ec2ByQuery => new GatewaySpec.Ec2(new Ec2Selector.ByQuery(new Ec2Query(
            string.IsNullOrWhiteSpace(GwNamePattern) ? null : Require(NamePattern.Create(GwNamePattern)),
            BuildTags(GwTags, "踏み台"),
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
                PopulateTags(DestTags, query.ResourceQuery.Tags);
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
                PopulateTags(GwTags, byQuery.Query.Tags);
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

    /// <summary>
    /// 入力行からコマンドを作る。名前もコマンドも空の行は無視し、
    /// 名前だけ埋まっている行はエラーにする。
    /// </summary>
    private LaunchCommands BuildCommands() =>
        LaunchCommands.From(
            Commands.Where(e => !e.IsBlank).Select(e => Require(LaunchCommand.Create(e.Label, e.CommandLine))));

    /// <summary>
    /// 入力行から TagFilters を作る。並べた行はすべて満たす必要がある (AND)。
    /// キーも値も空の行は無視し、片方だけ埋まっている行はエラーにする。
    /// </summary>
    private static TagFilters BuildTags(IEnumerable<TagEntryViewModel> entries, string label)
    {
        var filters = new List<TagFilter>();
        foreach (var entry in entries.Where(e => !e.IsBlank))
        {
            var filter = TagFilter.Create(entry.Key, entry.Value);
            if (filter.IsFailure)
                throw new FormException($"{label}のタグ条件: {filter.Error}");
            filters.Add(filter.Value);
        }

        return TagFilters.From(filters);
    }

    private void PopulateTags(ObservableCollection<TagEntryViewModel> target, TagFilters tags)
    {
        target.Clear();
        foreach (var filter in tags.Items)
            target.Add(new TagEntryViewModel(e => target.Remove(e)) { Key = filter.Key, Value = filter.Value });
    }
}
