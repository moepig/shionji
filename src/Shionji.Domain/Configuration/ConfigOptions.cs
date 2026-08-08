namespace Shionji.Domain.Configuration;

/// <summary>転送設定の動作オプション。</summary>
public sealed record ConfigOptions(bool AutoReconnect, bool ConnectOnLaunch)
{
    public static readonly ConfigOptions Default = new(AutoReconnect: false, ConnectOnLaunch: false);
}
