using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;
using Shionji.Infrastructure;
using Shionji.Infrastructure.Aws;
using Shionji.Infrastructure.Tunnel;

namespace Shionji.IntegrationTests;

/// <summary>
/// 偽 plugin + スタブ AWS で、実プロセス・実 TCP を使って
/// SessionManagerPluginLauncher を通しで動かすための足場。
/// </summary>
public sealed class TunnelHarness : IAsyncDisposable
{
    private readonly string _workDir;

    public StubAwsServer Aws { get; }
    public SessionManagerPluginLauncher Launcher { get; }
    public TcpLocalPortProbe PortProbe { get; } = new();

    /// <summary>偽 plugin が受け取った引数列 (起動後に読める)。</summary>
    public string ArgsFile { get; }

    private TunnelHarness(StubAwsServer aws, string workDir, string credentialsFile)
    {
        Aws = aws;
        _workDir = workDir;
        ArgsFile = Path.Combine(workDir, "plugin-args.json");

        var factory = new AwsClientFactory(endpointOverride: aws.Url, profilesLocation: credentialsFile);
        var locator = new SessionManagerPluginLocator(() => FakePluginPath);
        Launcher = new SessionManagerPluginLauncher(factory, locator, PortProbe);
    }

    public static async Task<TunnelHarness> CreateAsync(
        string mode = "normal", int? dropAfterMs = null, bool quiet = false)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"shionji-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        // 実プロファイル解決経路を通すため、一時的な資格情報ファイルを用意する
        var credentialsFile = Path.Combine(workDir, "credentials");
        await File.WriteAllTextAsync(credentialsFile, """
            [test]
            aws_access_key_id = AKIAIOSFODNN7EXAMPLE
            aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
            """);

        var harness = new TunnelHarness(
            (await StubAwsServer.StartAsync()).WithSsmSession(), workDir, credentialsFile);

        Environment.SetEnvironmentVariable("SHIONJI_FAKE_PLUGIN_MODE", mode);
        Environment.SetEnvironmentVariable("SHIONJI_FAKE_PLUGIN_ARGS_FILE", harness.ArgsFile);
        Environment.SetEnvironmentVariable("SHIONJI_FAKE_PLUGIN_QUIET", quiet ? "1" : null);
        Environment.SetEnvironmentVariable(
            "SHIONJI_FAKE_PLUGIN_DROP_AFTER_MS", dropAfterMs?.ToString());

        return harness;
    }

    public static string FakePluginPath =>
        Path.Combine(AppContext.BaseDirectory, "Shionji.FakePlugin.exe");

    public static AwsContext AwsContext() =>
        new(ProfileName.Create("test").Value, AwsRegion.Create("ap-northeast-1").Value);

    /// <summary>OS に割り当てさせた空きポートを使うトンネル計画。</summary>
    public TunnelPlan PlanForRemoteHost(string host = "db.example.internal", int remotePort = 5432)
    {
        var localPort = PortProbe.AcquireFreePort().Value;
        return new TunnelPlan(
            AwsContext(),
            new SsmTargetId("i-0123456789abcdef0"),
            new SessionMode.RemoteHostForward(HostName.Create(host).Value, Port.Create(remotePort).Value),
            localPort);
    }

    public TunnelPlan PlanForDirect(int remotePort = 22)
    {
        var localPort = PortProbe.AcquireFreePort().Value;
        return new TunnelPlan(
            AwsContext(),
            new SsmTargetId("i-0123456789abcdef0"),
            new SessionMode.DirectForward(Port.Create(remotePort).Value),
            localPort);
    }

    public string[] ReceivedPluginArgs() =>
        System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(ArgsFile))!;

    public async ValueTask DisposeAsync()
    {
        await Aws.DisposeAsync();
        foreach (var name in new[]
                 {
                     "SHIONJI_FAKE_PLUGIN_MODE", "SHIONJI_FAKE_PLUGIN_ARGS_FILE",
                     "SHIONJI_FAKE_PLUGIN_QUIET", "SHIONJI_FAKE_PLUGIN_DROP_AFTER_MS",
                 })
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
