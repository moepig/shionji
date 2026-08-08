using Shionji.Domain.Configuration;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Tunneling;

/// <summary>
/// 転送設定と解決済みリソースから <see cref="TunnelPlan"/> を導出する純粋なドメインサービス。
/// </summary>
public static class TunnelPlanner
{
    /// <param name="config">検証済みの転送設定。</param>
    /// <param name="destinationResource">クエリ転送先の解決結果。直接指定 (Static) の場合は null。</param>
    /// <param name="gatewayResource">クエリ踏み台の解決結果。Direct / インスタンス ID 直接指定の場合は null。</param>
    /// <param name="localPort">確定したローカルポート。</param>
    public static Result<TunnelPlan, ErrorDetail> CreatePlan(
        ForwardingConfig config,
        ResolvedResource? destinationResource,
        ResolvedResource? gatewayResource,
        Port localPort)
    {
        HostName? destinationHost;
        Port destinationPort;

        switch (config.Destination)
        {
            case Destination.Static s:
                destinationHost = s.Host;
                destinationPort = s.Port;
                break;

            case Destination.Query q:
                if (destinationResource is null)
                    return Fail(FailurePhase.ResolveDestination, "DestinationNotResolved", "転送先リソースが未解決です。");

                destinationHost = destinationResource.Host;
                switch (q.Port)
                {
                    case PortSelection.Explicit e:
                        destinationPort = e.Port;
                        break;
                    case PortSelection.FromResource when destinationResource.DefaultPort is { } defaultPort:
                        destinationPort = defaultPort;
                        break;
                    default:
                        return Fail(
                            FailurePhase.ResolveDestination,
                            "NoDefaultPort",
                            $"リソース「{destinationResource.DisplayName}」は既定ポートを持ちません。ポートを明示指定してください。");
                }
                break;

            default:
                throw new InvalidOperationException($"未知の転送先型: {config.Destination.GetType()}");
        }

        switch (config.Gateway)
        {
            case GatewaySpec.Direct:
            {
                if (destinationResource?.SsmTarget is not { } target)
                    return Fail(
                        FailurePhase.ResolveDestination,
                        "NotSessionCapable",
                        "転送先リソースに SSM セッションを張れません。");

                return Success(config, target, new SessionMode.DirectForward(destinationPort), localPort);
            }

            case GatewaySpec.Ec2 { Selector: Ec2Selector.ById byId }:
            {
                if (destinationHost is null)
                    return Fail(FailurePhase.ResolveDestination, "NoEndpoint", "転送先の接続エンドポイントが得られませんでした。");

                var target = SsmTargetId.ForInstance(byId.Id);
                return Success(config, target, new SessionMode.RemoteHostForward(destinationHost, destinationPort), localPort);
            }

            case GatewaySpec.Ec2 { Selector: Ec2Selector.ByQuery } or GatewaySpec.Ecs:
            {
                if (gatewayResource is null)
                    return Fail(FailurePhase.ResolveGateway, "GatewayNotResolved", "踏み台リソースが未解決です。");
                if (gatewayResource.SsmTarget is not { } target)
                    return Fail(FailurePhase.ResolveGateway, "NotSessionCapable", "踏み台リソースに SSM セッションを張れません。");
                if (destinationHost is null)
                    return Fail(FailurePhase.ResolveDestination, "NoEndpoint", "転送先の接続エンドポイントが得られませんでした。");

                return Success(config, target, new SessionMode.RemoteHostForward(destinationHost, destinationPort), localPort);
            }

            default:
                throw new InvalidOperationException($"未知の経路型: {config.Gateway.GetType()}");
        }
    }

    private static Result<TunnelPlan, ErrorDetail> Success(
        ForwardingConfig config, SsmTargetId target, SessionMode mode, Port localPort) =>
        Result<TunnelPlan, ErrorDetail>.Success(new TunnelPlan(config.Aws, target, mode, localPort));

    private static Result<TunnelPlan, ErrorDetail> Fail(FailurePhase phase, string code, string message) =>
        Result<TunnelPlan, ErrorDetail>.Failure(new ErrorDetail(phase, code, message));
}
