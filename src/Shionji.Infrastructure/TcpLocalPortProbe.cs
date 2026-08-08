using System.Net;
using System.Net.Sockets;
using Shionji.Domain.Ports;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure;

/// <summary>ループバックへの実バインドで空きを確認する。</summary>
public sealed class TcpLocalPortProbe : ILocalPortProbe
{
    public bool IsAvailable(Port port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port.Value);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public Result<Port, ErrorDetail> AcquireFreePort()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return Port.Create(port).Match(
                Result<Port, ErrorDetail>.Success,
                message => Result<Port, ErrorDetail>.Failure(
                    new ErrorDetail(FailurePhase.StartSession, "PortAllocation", message)));
        }
        catch (SocketException ex)
        {
            return Result<Port, ErrorDetail>.Failure(
                new ErrorDetail(FailurePhase.StartSession, "PortAllocation", $"空きポートの確保に失敗しました: {ex.Message}"));
        }
    }
}
