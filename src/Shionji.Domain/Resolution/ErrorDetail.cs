namespace Shionji.Domain.Resolution;

/// <summary>失敗が発生したフェーズ。UI のエラー文言出し分けに使う。</summary>
public enum FailurePhase
{
    /// <summary>資格情報の取得失敗 (SSO トークン期限切れなど)。</summary>
    Credentials,

    /// <summary>AWS API の権限不足。</summary>
    Permission,

    /// <summary>転送先リソースの解決失敗。</summary>
    ResolveDestination,

    /// <summary>踏み台リソースの解決失敗。</summary>
    ResolveGateway,

    /// <summary>ssm:StartSession の失敗。</summary>
    StartSession,

    /// <summary>session-manager-plugin の起動・実行失敗。</summary>
    Plugin,
}

/// <summary>フェーズ付きのエラー詳細。</summary>
public sealed record ErrorDetail(FailurePhase Phase, string Code, string Message);
