using Microsoft.Extensions.Logging;
using Shionji.Application;
using Shionji.Domain.Configuration;
using Shionji.Domain.Primitives;
using Shionji.Domain.Resolution;
using Shionji.Domain.Tunneling;
using Shionji.Domain.ValueObjects;

namespace Shionji.Presentation;

public sealed partial class MainViewModel
{
    private static ErrorDetail DatabaseAuthError(string code, string message) =>
        new(FailurePhase.DatabaseAuthentication, code, message);

    private static ErrorDetail ConnectionChangedError() => DatabaseAuthError(
        "ConnectionChanged", "接続状態または設定が変わりました。接続を確認してもう一度実行してください。");

    private bool IsCurrentConnection(ForwardingConfig config, SessionState.Established session) =>
        ReferenceEquals(_configService.Find(config.Id), config)
        && ReferenceEquals(_supervisor.GetState(config.Id), session);

    internal Task<Result<string, ErrorDetail>> GenerateRdsAuthTokenAsync(ConfigId id)
    {
        if (_configService.Find(id) is not { } config
            || _supervisor.GetState(id) is not SessionState.Established session)
            return Task.FromResult(Result<string, ErrorDetail>.Failure(ConnectionChangedError()));

        return GenerateRdsAuthTokenAsync(config, session);
    }

    private async Task<Result<string, ErrorDetail>> GenerateRdsAuthTokenAsync(
        ForwardingConfig config, SessionState.Established session)
    {
        if (config.RdsIamUserName is not { } userName
            || session.Plan.Mode is not SessionMode.RemoteHostForward remote)
            return Result<string, ErrorDetail>.Failure(DatabaseAuthError(
                "RdsIamNotConfigured", "接続先設定に RDS IAM 認証の DB ユーザー名を入力してください。"));

        // コピーとコマンド実行で共通のログ。取得ごとの ID で開始と結果を対応付ける。
        (string Key, object? Value)[] context =
        [
            ("operation", "RdsIamAuthentication"),
            ("authAttempt", Guid.NewGuid().ToString("N")),
            ("config", config.Name.Value),
            ("profile", $"{session.Plan.Aws.Profile.Value}@{session.Plan.Aws.Region.Value}"),
            ("destination", $"{remote.Host.Value}:{remote.RemotePort.Value}"),
            ("dbUser", userName.Value),
        ];
        _log.Audit(LogLevel.Information,
            $"[設定名: {config.Name.Value}] RDS IAM 認証トークンを取得しています…",
            [.. context, ("result", "Started")]);

        Result<string, ErrorDetail> result;
        try
        {
            // 検索結果のキャッシュではなく、現在のトンネルが使用中のエンドポイントで署名する。
            result = await _rdsAuthTokens.GenerateAsync(
                session.Plan.Aws, remote.Host, remote.RemotePort, userName);
            if (!IsCurrentConnection(config, session))
                result = Result<string, ErrorDetail>.Failure(ConnectionChangedError());
        }
        catch (Exception)
        {
            // トークンや資格情報を含む可能性のある例外本文は表示・記録しない。
            result = Result<string, ErrorDetail>.Failure(DatabaseAuthError(
                "TokenGenerationFailed", "RDS IAM 認証トークンを生成できませんでした。AWS プロファイルを確認してください。"));
        }

        // Result / ErrorDetail 全体や例外を渡さず、秘密を含まないフィールドだけを記録する。
        if (result.IsSuccess)
        {
            _log.Audit(LogLevel.Information,
                $"[設定名: {config.Name.Value}] RDS IAM 認証トークンを取得しました",
                [.. context, ("result", "Succeeded")]);
        }
        else
        {
            _log.Audit(LogLevel.Error,
                $"[設定名: {config.Name.Value}] RDS IAM 認証トークンの取得に失敗しました",
                [.. context, ("result", "Failed"), ("phase", result.Error.Phase), ("code", result.Error.Code)]);
        }

        return result;
    }

    internal async Task<ErrorDetail?> RunExternalCommandAsync(ConfigId id, LaunchCommand command)
    {
        if (_configService.Find(id) is not { } config
            || _supervisor.GetState(id) is not SessionState.Established session)
            return ConnectionChangedError();

        var needsPassword = CommandTemplate.NeedsPassword(command.CommandLine);
        if ((needsPassword || CommandTemplate.NeedsUserName(command.CommandLine))
            && config.RdsIamUserName is null)
            return DatabaseAuthError("RdsIamNotConfigured",
                "{username} / {password} を使うには RDS IAM 認証の DB ユーザー名を設定してください。");

        // 秘密値は引数だけで使用できる。実行ファイル名として OS のエラーへ渡さない。
        if (CommandTemplate.NeedsPassword(CommandTemplate.Split(command.CommandLine).FileName))
            return DatabaseAuthError("PasswordInFileName", "{password} はコマンドの引数に指定してください。");

        string? password = null;
        if (needsPassword)
        {
            var token = await GenerateRdsAuthTokenAsync(config, session);
            if (token.IsFailure)
                return token.Error;
            password = token.Value;
        }

        if (!IsCurrentConnection(config, session))
            return ConnectionChangedError();

        var (fileName, arguments) = CommandTemplate.Split(CommandTemplate.Expand(
            command.CommandLine, "localhost", session.Plan.LocalPort.Value,
            config.RdsIamUserName?.Value, password,
            (session.Plan.Mode as SessionMode.RemoteHostForward)?.Host.Value));

        try
        {
            var error = fileName.Length == 0 ? "コマンドが空です。" : _commandLauncher.Launch(fileName, arguments);
            return error is null ? null : new ErrorDetail(FailurePhase.Plugin, "CommandLaunchFailed",
                needsPassword ? "コマンドを実行できません。実行ファイルと引数を確認してください。" : error);
        }
        catch (Exception)
        {
            return new ErrorDetail(FailurePhase.Plugin, "CommandLaunchFailed",
                "コマンドを実行できません。実行ファイルと引数を確認してください。");
        }
    }
}
