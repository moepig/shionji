using System.Text.Json;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Infrastructure.Tunnel;

/// <summary>session-manager-plugin.exe へ渡す引数列の構築 (純粋関数)。</summary>
public static class PluginArguments
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    /// <summary>ssm:StartSession に渡すセッションドキュメントのパラメータ。</summary>
    public static Dictionary<string, List<string>> BuildParameters(SessionMode mode, Port localPort) => mode switch
    {
        SessionMode.DirectForward direct => new Dictionary<string, List<string>>
        {
            ["portNumber"] = [direct.RemotePort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            ["localPortNumber"] = [localPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)],
        },
        SessionMode.RemoteHostForward remote => new Dictionary<string, List<string>>
        {
            ["host"] = [remote.Host.Value],
            ["portNumber"] = [remote.RemotePort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            ["localPortNumber"] = [localPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)],
        },
        _ => throw new InvalidOperationException($"未知のセッション方式: {mode.GetType()}"),
    };

    /// <summary>
    /// plugin の引数列。AWS CLI と同じ順序:
    /// セッション応答 JSON / リージョン / "StartSession" / プロファイル名 / リクエスト JSON / SSM エンドポイント URL。
    /// </summary>
    public static string[] Build(
        TunnelPlan plan,
        string sessionId,
        string tokenValue,
        string streamUrl)
    {
        var sessionJson = JsonSerializer.Serialize(
            new { SessionId = sessionId, TokenValue = tokenValue, StreamUrl = streamUrl }, JsonOptions);
        var requestJson = JsonSerializer.Serialize(
            new
            {
                Target = plan.Target.Value,
                DocumentName = plan.Mode.DocumentName,
                Parameters = BuildParameters(plan.Mode, plan.LocalPort),
            }, JsonOptions);

        return
        [
            sessionJson,
            plan.Aws.Region.Value,
            "StartSession",
            plan.Aws.Profile.Value,
            requestJson,
            SsmEndpoint(plan.Aws.Region.Value),
        ];
    }

    public static string SsmEndpoint(string region) => $"https://ssm.{region}.amazonaws.com";
}
