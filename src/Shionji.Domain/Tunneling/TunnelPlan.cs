using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tunneling;

/// <summary>SSM セッションの転送方式。SSM ドキュメント名と 1 対 1 に対応する。</summary>
public abstract record SessionMode
{
    private SessionMode() { }

    public abstract string DocumentName { get; }

    /// <summary>セッションを張った相手自身のポートへ転送する。</summary>
    public sealed record DirectForward(Port RemotePort) : SessionMode
    {
        public override string DocumentName => "AWS-StartPortForwardingSession";
    }

    /// <summary>セッションを張った踏み台から別ホストへ転送する。</summary>
    public sealed record RemoteHostForward(HostName Host, Port RemotePort) : SessionMode
    {
        public override string DocumentName => "AWS-StartPortForwardingSessionToRemoteHost";
    }
}

/// <summary>セッション起動に必要な情報が完全に具体化されたトンネル計画。</summary>
public sealed record TunnelPlan(
    AwsContext Aws,
    SsmTargetId Target,
    SessionMode Mode,
    Port LocalPort);
