using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shionji.Application;
using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Presentation;

/// <summary>マスター・ディテール画面全体。行コレクション、フィルタ、選択、詳細ペインの切替を持つ。</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly TunnelSupervisor _supervisor;
    private readonly ResolutionService _resolution;
    private readonly IUiDispatcher _dispatcher;
    private readonly INotificationService _notifications;
    private readonly IClipboardService _clipboard;
    private readonly ISsoLoginService _ssoLogin;
    private readonly SessionLogStore _sessionLog;
    private readonly ILogLocationService _logLocation;

    private readonly Dictionary<ConfigId, ConfigRowViewModel> _rowsById = [];
    private readonly HashSet<ConfigId> _established = [];

    public ObservableCollection<ConfigRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ConfigRowViewModel? SelectedRow { get; set; }

    /// <summary>右ペインの中身。ConfigDetailViewModel または ConfigEditorViewModel。</summary>
    [ObservableProperty]
    public partial ObservableObject? DetailContent { get; set; }

    // --- ステータスバー ---

    /// <summary>最新の動作。ステータスバーに 1 行で出す。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "準備中…";

    [ObservableProperty]
    public partial ActivitySeverity StatusSeverity { get; set; } = ActivitySeverity.Info;

    [ObservableProperty]
    public partial string StatusTime { get; set; } = string.Empty;

    /// <summary>履歴一覧 (新しい順)。ステータスバーから開く。</summary>
    public ObservableCollection<ActivityItemViewModel> Activities { get; } = [];

    public string LogDirectory => _logLocation.LogDirectory;

    [RelayCommand]
    private void OpenLogLocation() => _logLocation.OpenLogLocation();

    public MainViewModel(
        ConfigService configService,
        TunnelSupervisor supervisor,
        ResolutionService resolution,
        IUiDispatcher dispatcher,
        INotificationService notifications,
        IClipboardService clipboard,
        ISsoLoginService ssoLogin,
        SessionLogStore sessionLog,
        ActivityLog activityLog,
        ILogLocationService logLocation)
    {
        _configService = configService;
        _supervisor = supervisor;
        _resolution = resolution;
        _dispatcher = dispatcher;
        _notifications = notifications;
        _clipboard = clipboard;
        _ssoLogin = ssoLogin;
        _sessionLog = sessionLog;
        _logLocation = logLocation;

        foreach (var entry in activityLog.Recent)
            AppendActivity(entry);
        activityLog.Posted += (_, entry) => _dispatcher.Post(() => AppendActivity(entry));

        _configService.ConfigsChanged += (_, _) => _dispatcher.Post(RebuildRows);
        _supervisor.SessionChanged += (_, e) => _dispatcher.Post(() => OnSessionChanged(e));
        _sessionLog.LineAppended += (_, e) =>
            _dispatcher.Post(() => (DetailContent as ConfigDetailViewModel)?.AppendLog(e));
        _resolution.ViewChanged += (_, id) => _dispatcher.Post(() => RefreshConfig(id));
    }

    partial void OnFilterTextChanged(string value) => RebuildRows();

    partial void OnSelectedRowChanged(ConfigRowViewModel? value)
    {
        if (value is null)
        {
            if (DetailContent is ConfigDetailViewModel)
                DetailContent = null;
            return;
        }

        var detail = new ConfigDetailViewModel(this, value.ConfigId, _clipboard);
        detail.LoadLog(_sessionLog.GetLines(value.ConfigId));
        DetailContent = detail;
        RefreshDetail(detail);
    }

    [RelayCommand]
    private void AddConfig()
    {
        SelectedRow = null;
        DetailContent = ConfigEditorViewModel.ForNew(this);
    }

    [RelayCommand]
    private Task RefreshAllAsync() => _resolution.RefreshAllAsync(_configService.Configs);

    internal async Task ToggleConnectionAsync(ConfigId id)
    {
        if (_supervisor.GetState(id) is SessionState.Idle or SessionState.Failed)
        {
            if (_configService.Find(id) is { } config)
                await _supervisor.StartAsync(config);
        }
        else
        {
            await _supervisor.StopAsync(id);
        }
    }

    internal async Task RefreshResolutionAsync(ConfigId id)
    {
        if (_configService.Find(id) is { } config)
            await _resolution.RefreshAsync(config);
    }

    /// <summary>
    /// ブラウザ承認込みの SSO ログイン。成功したら再解決し、
    /// 資格情報エラーで失敗していたセッションは接続をやり直す。
    /// </summary>
    internal async Task<ErrorDetail?> SsoLoginAsync(ConfigId id)
    {
        if (_configService.Find(id) is not { } config)
            return null;

        var error = await _ssoLogin.LoginAsync(config.Aws.Profile);
        if (error is not null)
            return error;

        var retryConnect = _supervisor.GetState(id) is SessionState.Failed
        {
            Error.Phase: FailurePhase.Credentials,
        };

        await _resolution.RefreshAsync(config);
        if (retryConnect)
            await _supervisor.StartAsync(config);

        return null;
    }

    internal Task SaveConfigAsync(Domain.Configuration.ForwardingConfig config) =>
        _configService.SaveAsync(config);

    internal async Task DeleteConfigAsync(ConfigId id)
    {
        await _configService.DeleteAsync(id);
        _dispatcher.Post(() =>
        {
            if (SelectedRow?.ConfigId == id)
                SelectedRow = null;
        });
    }

    internal void ShowEditor(ConfigId id)
    {
        if (_configService.Find(id) is { } config)
            DetailContent = ConfigEditorViewModel.ForExisting(this, config);
    }

    /// <summary>編集終了。保存された場合はその行を選択し詳細に戻る。</summary>
    internal void CloseEditor(ConfigId? savedId)
    {
        _dispatcher.Post(() =>
        {
            RebuildRows();
            if (savedId is { } id && _rowsById.TryGetValue(id, out var row))
            {
                SelectedRow = row;
            }
            else if (SelectedRow is { } selected)
            {
                // 詳細表示に戻す
                OnSelectedRowChanged(selected);
            }
            else
            {
                DetailContent = null;
            }
        });
    }

    /// <summary>
    /// 行コレクションを差分更新する。Clear → 全再追加は ListView の選択と
    /// スクロール位置を壊すため、挿入 / 移動 / 削除の最小操作で並びを合わせる。
    /// </summary>
    private void RebuildRows()
    {
        var snapshot = _configService.Configs;
        var desired = snapshot
            .Where(c => FilterText.Length == 0 ||
                        c.Name.Value.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var aliveIds = snapshot.Select(c => c.Id).ToHashSet();
        foreach (var stale in _rowsById.Keys.Where(id => !aliveIds.Contains(id)).ToList())
            _rowsById.Remove(stale);

        var desiredIds = desired.Select(c => c.Id).ToHashSet();
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!desiredIds.Contains(Rows[i].ConfigId))
                Rows.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var config = desired[i];
            if (!_rowsById.TryGetValue(config.Id, out var row))
            {
                row = new ConfigRowViewModel(this, config.Id);
                _rowsById[config.Id] = row;
            }

            UpdateRow(row, config);

            var currentIndex = Rows.IndexOf(row);
            if (currentIndex == i)
                continue;
            if (currentIndex < 0)
                Rows.Insert(i, row);
            else
                Rows.Move(currentIndex, i);
        }
    }

    private void OnSessionChanged(SessionChangedEventArgs e)
    {
        NotifyIfUnexpectedDisconnect(e);
        RefreshConfig(e.ConfigId);
    }

    private void NotifyIfUnexpectedDisconnect(SessionChangedEventArgs e)
    {
        if (e.State is SessionState.Established)
        {
            _established.Add(e.ConfigId);
            return;
        }

        var wasEstablished = _established.Remove(e.ConfigId);
        if (!wasEstablished)
            return;

        var name = _configService.Find(e.ConfigId)?.Name.Value ?? e.ConfigId.ToString();
        switch (e.State)
        {
            case SessionState.Reconnecting reconnecting:
                _notifications.NotifyUnexpectedDisconnect(
                    name, $"接続が切断されました。{reconnecting.Delay.TotalSeconds:0} 秒後に再接続します。");
                break;
            case SessionState.Failed failed:
                _notifications.NotifyUnexpectedDisconnect(name, failed.Error.Message);
                break;
        }
    }

    /// <summary>ステータスバーの表示を最新にし、履歴の先頭へ積む。</summary>
    private void AppendActivity(ActivityEntry entry)
    {
        var item = new ActivityItemViewModel(entry);
        StatusText = item.Message;
        StatusSeverity = item.Severity;
        StatusTime = item.Time;

        Activities.Insert(0, item);
        while (Activities.Count > 200)
            Activities.RemoveAt(Activities.Count - 1);
    }

    private void RefreshConfig(ConfigId id)
    {
        if (_rowsById.TryGetValue(id, out var row))
            UpdateRow(row);

        if (DetailContent is ConfigDetailViewModel detail && detail.ConfigId == id)
            RefreshDetail(detail);
    }

    private void UpdateRow(ConfigRowViewModel row)
    {
        if (_configService.Find(row.ConfigId) is { } config)
            UpdateRow(row, config);
    }

    private void UpdateRow(ConfigRowViewModel row, Domain.Configuration.ForwardingConfig config)
    {
        row.Update(
            config,
            _supervisor.GetState(row.ConfigId),
            _resolution.GetView(row.ConfigId),
            _supervisor.GetLocalPort(row.ConfigId));
    }

    private void RefreshDetail(ConfigDetailViewModel detail)
    {
        if (_configService.Find(detail.ConfigId) is not { } config)
            return;

        detail.Refresh(
            config,
            _supervisor.GetState(detail.ConfigId),
            _resolution.GetView(detail.ConfigId),
            _supervisor.GetLocalPort(detail.ConfigId));
    }
}
