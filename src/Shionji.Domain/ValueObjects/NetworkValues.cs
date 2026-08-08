using Shionji.Domain.Primitives;

namespace Shionji.Domain.ValueObjects;

/// <summary>TCP ポート番号 (1〜65535)。</summary>
public sealed record Port
{
    public int Value { get; }

    private Port(int value) => Value = value;

    public static Result<Port, string> Create(int value) =>
        value is >= 1 and <= 65535
            ? Result<Port, string>.Success(new Port(value))
            : Result<Port, string>.Failure($"ポート番号は 1〜65535 で指定してください: {value}");

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>接続先ホスト名または IP アドレス。</summary>
public sealed record HostName
{
    public string Value { get; }

    private HostName(string value) => Value = value;

    public static Result<HostName, string> Create(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result<HostName, string>.Failure("ホスト名を入力してください。");
        if (trimmed.Length > 253)
            return Result<HostName, string>.Failure("ホスト名が長すぎます (253 文字以内)。");
        if (trimmed.Any(char.IsWhiteSpace))
            return Result<HostName, string>.Failure("ホスト名に空白は使用できません。");
        return Result<HostName, string>.Success(new HostName(trimmed));
    }

    public override string ToString() => Value;
}
