using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Ports;

public enum AwsAuthenticationState
{
    NotAuthenticated,
    TemporaryCredentials,
    LongTermCredentials,
    CheckFailed,
}

/// <summary>秘密情報を含まない、対象プロファイルの認証確認結果。</summary>
public sealed record AwsAuthenticationStatus(AwsAuthenticationState State, string Message);

public enum AwsLoginStage
{
    OpeningBrowser,
    WaitingForApproval,
    ProcessExited,
}

/// <summary>CLI の生出力・URL・資格情報を含めずに通知する進行状況。</summary>
public sealed record AwsLoginProgress(AwsLoginStage Stage, int? ExitCode = null);

public interface IAwsAuthenticationService
{
    /// <summary>ユーザー操作から aws login を実行し、ブラウザでの承認を待つ。</summary>
    Task<ErrorDetail?> LoginAsync(
        AwsContext aws, IProgress<AwsLoginProgress> progress, CancellationToken cancellationToken = default);

    /// <summary>ブラウザを開かず、現在の資格情報を AWS に照会する。</summary>
    Task<AwsAuthenticationStatus> GetStatusAsync(AwsContext aws, CancellationToken cancellationToken = default);
}
