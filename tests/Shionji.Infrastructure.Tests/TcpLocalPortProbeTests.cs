using System.Net;
using System.Net.Sockets;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Tests;

// 実ポートを掴むため直列実行する
[NotInParallel]
public class TcpLocalPortProbeTests
{
    private static readonly TcpLocalPortProbe Probe = new();

    [Test]
    public async Task 空きポートを割り当てられその時点では未使用()
    {
        var acquired = Probe.AcquireFreePort();

        await Assert.That(acquired.IsSuccess).IsTrue();
        await Assert.That(Probe.IsAvailable(acquired.Value)).IsTrue();
        await Assert.That(Probe.IsListening(acquired.Value)).IsFalse();
    }

    [Test]
    public async Task 使用中のポートは利用不可かつlistening()
    {
        var port = Probe.AcquireFreePort().Value;
        var listener = new TcpListener(IPAddress.Loopback, port.Value);
        listener.Start();
        try
        {
            await Assert.That(Probe.IsAvailable(port)).IsFalse();
            await Assert.That(Probe.IsListening(port)).IsTrue();
        }
        finally
        {
            listener.Stop();
        }

        // 解放後は元に戻る
        await Assert.That(Probe.IsListening(port)).IsFalse();
    }

    [Test]
    public async Task 連続して取得すると異なるポートになる()
    {
        var a = Probe.AcquireFreePort().Value;
        var listener = new TcpListener(IPAddress.Loopback, a.Value);
        listener.Start();
        try
        {
            var b = Probe.AcquireFreePort().Value;
            await Assert.That(b.Value).IsNotEqualTo(a.Value);
        }
        finally
        {
            listener.Stop();
        }
    }
}
