using System.Text.RegularExpressions;
using Shionji.Domain.Primitives;

namespace Shionji.Domain.ValueObjects;

/// <summary>
/// リソース名の glob パターン。「*」は任意の文字列、「?」は任意の 1 文字に一致する。
/// 大文字小文字は区別しない。
/// </summary>
public sealed record NamePattern
{
    public string Value { get; }

    private readonly Regex _regex;

    private NamePattern(string value)
    {
        Value = value;
        _regex = ToRegex(value);
    }

    public static Result<NamePattern, string> Create(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result<NamePattern, string>.Failure("名前パターンを入力してください。");
        return Result<NamePattern, string>.Success(new NamePattern(trimmed));
    }

    public bool IsMatch(string candidate) => _regex.IsMatch(candidate);

    private static Regex ToRegex(string glob)
    {
        var pattern = "^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public bool Equals(NamePattern? other) => other is not null && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value;
}
