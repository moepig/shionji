using Shionji.Domain.Ports;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Fakes;

/// <summary>デモモード内で共有する疑似ログイン状態。</summary>
public sealed class FakeSsoState
{
    private readonly HashSet<string> _loggedIn = [];
    private readonly object _sync = new();

    public bool IsLoggedIn(string profile)
    {
        lock (_sync)
        {
            return _loggedIn.Contains(profile);
        }
    }

    public void MarkLoggedIn(string profile)
    {
        lock (_sync)
        {
            _loggedIn.Add(profile);
        }
    }
}

/// <summary>ブラウザ承認の待ち時間を模して数秒後にログイン済みにする。</summary>
public sealed class FakeSsoLoginService(FakeSsoState state) : ISsoLoginService
{
    public async Task<ErrorDetail?> LoginAsync(ProfileName profile, CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        state.MarkLoggedIn(profile.Value);
        return null;
    }
}
