using Shionji.Domain.Primitives;

namespace Shionji.Domain.ValueObjects;

/// <summary>転送設定の識別子。</summary>
public sealed record ConfigId(Guid Value)
{
    public static ConfigId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

/// <summary>転送設定の表示名。</summary>
public sealed record ConfigName
{
    public string Value { get; }

    private ConfigName(string value) => Value = value;

    public static Result<ConfigName, string> Create(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result<ConfigName, string>.Failure("設定名を入力してください。");
        if (trimmed.Length > 64)
            return Result<ConfigName, string>.Failure("設定名が長すぎます (64 文字以内)。");
        return Result<ConfigName, string>.Success(new ConfigName(trimmed));
    }

    public override string ToString() => Value;
}
