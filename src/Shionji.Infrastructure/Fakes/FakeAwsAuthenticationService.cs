using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Fakes;

/// <summary>デモでは CLI・ブラウザ・実 AWS へアクセスしない。</summary>
public sealed class FakeAwsAuthenticationService(FakeSsoState state) : IAwsAuthenticationService
{
    public async Task<ErrorDetail?> LoginAsync(
        AwsContext aws, IProgress<AwsLoginProgress> progress, CancellationToken cancellationToken = default)
    {
        progress.Report(new(AwsLoginStage.WaitingForApproval));
        await Task.Delay(800, cancellationToken);
        state.MarkLoggedIn(aws.Profile.Value);
        progress.Report(new(AwsLoginStage.ProcessExited, 0));
        return null;
    }

    public Task<AwsAuthenticationStatus> GetStatusAsync(AwsContext aws, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AwsAuthenticationStatus(
            state.IsLoggedIn(aws.Profile.Value)
                ? AwsAuthenticationState.TemporaryCredentials
                : AwsAuthenticationState.NotAuthenticated,
            "デモモードの認証状態です。"));
}
