using Shionji.Domain.Resolution;
using Shionji.Infrastructure.Tunnel;

namespace Shionji.Infrastructure.Tests;

// PATH 環境変数をプロセス全体で差し替えるため直列実行する
[NotInParallel]
public class PluginLocatorTests
{
    private const string ExeName = "session-manager-plugin.exe";

    /// <summary>PATH を差し替えて locator の探索を検証する。</summary>
    private static async Task WithPathAsync(string? path, Func<Task> body)
    {
        var original = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", path);
        try
        {
            await body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }

    [Test]
    public async Task 設定パスが存在すればそれを使う()
    {
        using var dir = new TempDir();
        var configured = dir.File("my-plugin.exe");
        await File.WriteAllTextAsync(configured, "");

        var located = new SessionManagerPluginLocator(() => configured).Locate();

        await Assert.That(located.IsSuccess).IsTrue();
        await Assert.That(located.Value).IsEqualTo(configured);
    }

    [Test]
    public async Task 設定パスが存在しなければ次の候補へ進む()
    {
        using var dir = new TempDir();
        var onPath = dir.File(ExeName);
        await File.WriteAllTextAsync(onPath, "");
        var missing = dir.File("does-not-exist.exe");

        await WithPathAsync(dir.Path, async () =>
        {
            var located = new SessionManagerPluginLocator(() => missing).Locate();

            await Assert.That(located.IsSuccess).IsTrue();
            await Assert.That(located.Value).IsEqualTo(onPath);
        });
    }

    [Test]
    public async Task PATH上のディレクトリから見つけられる()
    {
        using var empty = new TempDir();
        using var dir = new TempDir();
        var onPath = dir.File(ExeName);
        await File.WriteAllTextAsync(onPath, "");

        // 先頭に無関係なディレクトリを並べても後続まで探す
        await WithPathAsync($"{empty.Path}{Path.PathSeparator}{dir.Path}", async () =>
        {
            var located = new SessionManagerPluginLocator().Locate();

            await Assert.That(located.IsSuccess).IsTrue();
            await Assert.That(located.Value).IsEqualTo(onPath);
        });
    }

    [Test]
    public async Task どこにも無ければインストール案内付きのエラーになる()
    {
        using var empty = new TempDir();

        await WithPathAsync(empty.Path, async () =>
        {
            var located = new SessionManagerPluginLocator(() => null).Locate();

            await Assert.That(located.IsFailure).IsTrue();
            await Assert.That(located.Error.Phase).IsEqualTo(FailurePhase.Plugin);
            await Assert.That(located.Error.Code).IsEqualTo("PluginNotFound");
            await Assert.That(located.Error.Message).Contains(SessionManagerPluginLocator.InstallGuideUrl);
        });
    }

    [Test]
    public async Task 設定パスが空文字なら未指定として扱う()
    {
        using var dir = new TempDir();
        var onPath = dir.File(ExeName);
        await File.WriteAllTextAsync(onPath, "");

        await WithPathAsync(dir.Path, async () =>
        {
            var located = new SessionManagerPluginLocator(() => string.Empty).Locate();

            await Assert.That(located.IsSuccess).IsTrue();
            await Assert.That(located.Value).IsEqualTo(onPath);
        });
    }

    [Test]
    public async Task PATHが未設定でも落ちない()
    {
        await WithPathAsync(null, async () =>
        {
            var located = new SessionManagerPluginLocator(() => null).Locate();

            // 実インストールがある環境では成功しうるので、例外を投げないことだけを確認する
            await Assert.That(located.IsSuccess || located.Error.Code == "PluginNotFound").IsTrue();
        });
    }
}
