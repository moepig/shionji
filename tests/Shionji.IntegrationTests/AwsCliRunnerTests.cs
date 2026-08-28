using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Shionji.Application;
using Shionji.Infrastructure.Aws;
using Shionji.Infrastructure.Logging;
using Shionji.Presentation;
using Shionji.TestSupport;

namespace Shionji.IntegrationTests;

public class AwsCliRunnerTests
{
    [Test]
    public async Task 標準出力と標準エラーを両方読み改行まで分割された認証URLも受け取る()
    {
        var lines = new List<string>();
        var runner = new AwsCliRunner("unused", TunnelHarness.FakePluginPath);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var exitCode = await runner.RunAsync(["login"], lines.Add, cancellation.Token);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(lines.Count(line => line.StartsWith("out:"))).IsEqualTo(1000);
        await Assert.That(lines.Count(line => line.StartsWith("err:"))).IsEqualTo(1000);
        await Assert.That(lines.Any(line => line.EndsWith("/v1/authorize?state=test"))).IsTrue();
        await Assert.That(lines.Contains("認証が完了しました")).IsTrue();
    }

    [Test]
    public async Task 改行なしのプロンプトも取得して上書きを承諾せずに終了する()
    {
        var lines = new List<string>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var exitCode = await new AwsCliRunner("unused", TunnelHarness.FakePluginPath)
            .RunAsync(["login", "prompt"], lines.Add, cancellation.Token);

        await Assert.That(exitCode).IsEqualTo(2);
        await Assert.That(lines.Last()).Contains("(y/n)");
    }

    [Test]
    public async Task キャンセルで待機中の実プロセスを終了する()
    {
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = new AwsCliRunner("unused", TunnelHarness.FakePluginPath).RunAsync(["login", "hang"], line =>
        {
            if (line.StartsWith("PID:"))
                started.TrySetResult(int.Parse(line[4..]));
        }, cancellation.Token);
        var processId = await started.Task.WaitAsync(cancellation.Token);
        using var process = Process.GetProcessById(processId);

        cancellation.Cancel();
        await Assert.That(async () => await run).Throws<OperationCanceledException>();

        await Assert.That(process.HasExited).IsTrue();
    }

    [Test]
    public async Task 出力処理の例外でも実プロセスを残さない()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = new AwsCliRunner("unused", TunnelHarness.FakePluginPath).RunAsync(["login", "hang"],
            _ => throw new InvalidOperationException("output failure"), cancellation.Token);

        await Assert.That(async () => await run).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CLIの秘密情報は画面にも実ログファイルにも書かれない(bool fail)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shionji-auth-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "credentials"), "");
            var activity = new ActivityLog(new FakeClock());
            using var factory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new FileLoggerProvider(directory));
                builder.AddProvider(new ActivityLogProvider(activity));
            });
            var browserCount = 0;
            var service = new AwsAuthenticationService(
                new AwsClientFactory(profilesLocation: Path.Combine(directory, "credentials")),
                new CliWithSensitiveOutput(fail), _ => browserCount++,
                (_, _) => throw new InvalidOperationException("資格情報がないので STS は呼ばない"));
            var model = new AwsAuthenticationViewModel(service, factory.CreateLogger<AwsAuthenticationViewModel>(), new ImmediateUi());

            await model.LoginCommand.ExecuteAsync(null);
            await model.StopAsync();

            var lines = await File.ReadAllLinesAsync(Directory.GetFiles(directory, "*.log").Single());
            var fileLog = string.Join("\n", lines);
            var screenLog = string.Join("\n", activity.Recent.Select(entry => entry.Message));
            await Assert.That(browserCount).IsEqualTo(1);
            await Assert.That(lines.Length).IsEqualTo(activity.Recent.Count);
            foreach (var log in new[] { fileLog, screenLog })
            {
                await Assert.That(log).Contains("aws login を開始");
                await Assert.That(log).Contains("承認を待っています");
                await Assert.That(log).Contains($"終了コード: {(fail ? 31 : 0)}");
                await Assert.That(log).Contains("認証状態:");
                await Assert.That(log).DoesNotContain("DO_NOT_LOG");
                await Assert.That(log).DoesNotContain("https://");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CliWithSensitiveOutput(bool fail) : IAwsCliRunner
    {
        public Task<int> RunAsync(IReadOnlyList<string> arguments, Action<string> output, CancellationToken cancellationToken) =>
            new AwsCliRunner("unused", TunnelHarness.FakePluginPath).RunAsync(
                [.. arguments, "sensitive-output", fail ? "login-failure" : "login-success"], output, cancellationToken);
    }

    private sealed class ImmediateUi : IUiDispatcher
    {
        public void Post(Action action) => action();
    }
}
