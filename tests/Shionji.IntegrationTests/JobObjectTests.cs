using System.Diagnostics;
using System.Runtime.InteropServices;
using Shionji.Infrastructure.Tunnel;

namespace Shionji.IntegrationTests;

/// <summary>
/// 起動した plugin が KILL_ON_JOB_CLOSE の Job Object に入っていることを確認する。
/// 割り当ての P/Invoke は戻り値を無視するため、失敗しても普段は気づけない。
/// ここが崩れるとアプリ異常終了時に plugin が孤児として残る。
/// </summary>
[NotInParallel]
public partial class JobObjectTests
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsProcessInJob(nint process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [Test]
    public async Task Jobオブジェクトの作成に成功している()
    {
        await Assert.That(WindowsJobObject.JobHandle).IsNotEqualTo(0);
    }

    [Test]
    public async Task 起動したpluginはJobに収容される()
    {
        await using var harness = await TunnelHarness.CreateAsync();
        var plan = harness.PlanForRemoteHost();

        var launched = await harness.Launcher.LaunchAsync(plan);
        await Assert.That(launched.IsSuccess).IsTrue();
        await using var handle = launched.Value;

        // 起動された plugin プロセスを特定する
        var plugin = Process.GetProcessesByName("Shionji.FakePlugin")
            .FirstOrDefault(p => !p.HasExited);
        await Assert.That(plugin).IsNotNull();

        var inJob = IsProcessInJob(plugin!.Handle, WindowsJobObject.JobHandle, out var result);

        await Assert.That(inJob).IsTrue();
        await Assert.That(result).IsTrue();
    }
}
