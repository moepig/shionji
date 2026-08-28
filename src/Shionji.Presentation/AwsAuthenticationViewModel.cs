using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Shionji.Domain.Ports;
using Shionji.Domain.ValueObjects;

namespace Shionji.Presentation;

/// <summary>ログとは独立して、現在の対象プロファイルの認証状態を表示する。</summary>
public sealed partial class AwsAuthenticationViewModel(
    IAwsAuthenticationService service, ILogger<AwsAuthenticationViewModel> logger, IUiDispatcher dispatcher) : ObservableObject
{
    private static readonly AwsContext DefaultContext = new(
        ProfileName.Create("default").Value, AwsRegion.Create("ap-northeast-1").Value);
    private AwsContext _context = DefaultContext;
    private CancellationTokenSource? _checkCancellation;
    private CancellationTokenSource? _loginCancellation;
    private Task? _checkTask;
    private int _revision;
    private bool _started;
    private bool _stopping;
    private (AwsContext Context, AwsAuthenticationState State)? _lastLoggedStatus;

    public string TargetText => $"{_context.Profile.Value} / {_context.Region.Value}";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "未確認";

    [ObservableProperty]
    public partial string DetailText { get; set; } = "接続先を選ぶと、そのプロファイルの認証状態を表示します。";

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsLoggingIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsChecking { get; set; }

    public bool IsBusy => IsLoggingIn || IsChecking;
    private bool CanLogin => !IsLoggingIn && !_stopping;

    public Task StartAsync()
    {
        _started = true;
        return RefreshAsync();
    }

    public void SetContext(AwsContext? context)
    {
        context ??= DefaultContext;
        if (_context == context)
            return;

        _context = context;
        _revision++;
        _checkCancellation?.Cancel();
        // 承認する対象と表示対象が食い違わないよう、切替時は処理を取り消す。
        _loginCancellation?.Cancel();
        OnPropertyChanged(nameof(TargetText));
        IsAuthenticated = false;
        ErrorText = null;
        StatusText = "未確認";
        DetailText = "認証状態を確認します。";
        if (_started && !IsLoggingIn)
            _ = RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private Task RefreshAsync()
    {
        if (!_started || _stopping || IsLoggingIn)
            return Task.CompletedTask;
        if (_checkTask is { IsCompleted: false } && _checkCancellation?.IsCancellationRequested == false)
            return _checkTask;

        _checkCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _checkCancellation = cancellation;
        _checkTask = CheckAsync(_context, ++_revision, cancellation);
        return _checkTask;
    }

    private async Task CheckAsync(AwsContext context, int revision, CancellationTokenSource cancellation)
    {
        IsChecking = true;
        StatusText = "確認中…";
        IsAuthenticated = false;
        try
        {
            var status = await service.GetStatusAsync(context, cancellation.Token);
            if (revision != _revision || _stopping)
                return;
            IsAuthenticated = status.State == AwsAuthenticationState.TemporaryCredentials;
            StatusText = StateLabel(status.State);
            DetailText = $"{status.Message}（確認: {DateTimeOffset.Now:HH:mm:ss}）";
            LogStatus(context, status.State);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (revision == _revision && !_stopping)
            {
                StatusText = "確認できません";
                DetailText = "認証状態の確認に失敗しました。再確認してください。";
                LogStatus(context, AwsAuthenticationState.CheckFailed);
            }
        }
        finally
        {
            if (ReferenceEquals(_checkCancellation, cancellation))
            {
                _checkCancellation = null;
                IsChecking = false;
            }
            cancellation.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanLogin), IncludeCancelCommand = true)]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        if (!CanLogin)
            return;
        var context = _context;
        var revision = ++_revision;
        _checkCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loginCancellation = cancellation;
        IsLoggingIn = true;
        IsAuthenticated = false;
        ErrorText = null;
        StatusText = "ログイン中…";
        DetailText = "aws login を起動しています…";
        WriteLog(LogLevel.Information, context, "aws login を開始します");
        try
        {
            var progress = new LoginProgress(update =>
            {
                var message = update.Stage switch
                {
                    AwsLoginStage.OpeningBrowser => "認証用ブラウザを開いています…",
                    AwsLoginStage.WaitingForApproval => "ブラウザでの承認を待っています…",
                    AwsLoginStage.ProcessExited when update.ExitCode is { } code => $"aws login が終了しました（終了コード: {code}）",
                    _ => null,
                };
                if (message is null)
                    return;

                // UI の更新待ち中にログインが終わっても、進行ログは欠落させない。
                var level = update.Stage == AwsLoginStage.ProcessExited && update.ExitCode != 0
                    ? LogLevel.Warning : LogLevel.Information;
                WriteLog(level, context, message);
                dispatcher.Post(() =>
                {
                    if (revision == _revision && IsLoggingIn && !_stopping)
                        DetailText = message;
                });
            });
            var error = await service.LoginAsync(context, progress, cancellation.Token);
            if (error is not null)
            {
                if (revision == _revision)
                    ErrorText = error.Message;
                // Message や例外には将来 CLI の出力が含まれうるため、ログには既知の分類だけを使う。
                WriteLog(LogLevel.Error, context, FailureLabel(error.Code));
            }
            else
            {
                WriteLog(LogLevel.Information, context, "aws login が完了しました。認証状態を確認します");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            WriteLog(LogLevel.Warning, context, "ログインをキャンセルしました");
        }
        catch (Exception)
        {
            if (revision == _revision)
                ErrorText = "ログイン処理に失敗しました。もう一度実行してください。";
            WriteLog(LogLevel.Error, context, "ログイン処理に失敗しました");
        }
        finally
        {
            _loginCancellation = null;
            IsLoggingIn = false;
            // 終了コードだけでは認証済みにしない。失敗・キャンセル時も実際の状態を再確認する。
            _started = true;
            await RefreshAsync();
        }
    }

    public async Task StopAsync()
    {
        _stopping = true;
        _revision++;
        _checkCancellation?.Cancel();
        _loginCancellation?.Cancel();
        LoginCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        await Task.WhenAll(_checkTask ?? Task.CompletedTask, LoginCommand.ExecutionTask ?? Task.CompletedTask);
    }

    private void LogStatus(AwsContext context, AwsAuthenticationState state)
    {
        // 定期確認で同じ状態を繰り返し出し、ほかの動作ログを流してしまわない。
        if (_lastLoggedStatus is { } previous && previous.Context == context && previous.State == state)
            return;
        _lastLoggedStatus = (context, state);
        WriteLog(state is AwsAuthenticationState.NotAuthenticated or AwsAuthenticationState.CheckFailed
                ? LogLevel.Warning : LogLevel.Information,
            context, $"認証状態: {StateLabel(state)}");
    }

    // 呼び出し元で作った固定文言のみを渡す。生出力・Status.Message・例外は受け渡さない。
    private void WriteLog(LogLevel level, AwsContext context, string message) =>
        logger.Log(level, "[AWS 認証: {Profile} / {Region}] {Message}",
            LogName(context.Profile.Value), LogName(context.Region.Value), message);

    private static string LogName(string value) =>
        new(value.Take(128).Select(c => char.IsControl(c) ? ' ' : c).ToArray());

    private static string StateLabel(AwsAuthenticationState state) => state switch
    {
        AwsAuthenticationState.TemporaryCredentials => "一時認証済み",
        AwsAuthenticationState.LongTermCredentials => "一時認証なし（長期キー使用中）",
        AwsAuthenticationState.NotAuthenticated => "未認証・期限切れ",
        _ => "確認できません",
    };

    private static string FailureLabel(string code) => code switch
    {
        "AwsCliNotFound" => "AWS CLI が見つかりません",
        "AwsLoginUnsupported" => "AWS CLI が aws login に対応していません",
        "BrowserOpenFailed" => "認証用ブラウザの起動に失敗しました",
        "AwsLoginTimeout" => "ログインがタイムアウトしました",
        "InteractiveInputRequired" => "プロファイルの変更確認が必要なためログインを中止しました",
        "IncompatibleProfile" => "プロファイルに別方式の資格情報が設定されています",
        _ => "aws login の実行に失敗しました",
    };

    private sealed class LoginProgress(Action<AwsLoginProgress> report) : IProgress<AwsLoginProgress>
    {
        public void Report(AwsLoginProgress value) => report(value);
    }
}
