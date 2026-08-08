using Shionji.Domain.Primitives;

namespace Shionji.Domain.ValueObjects;

/// <summary>タグ 1 件の一致条件。キーと値が完全に一致すれば充足。</summary>
public sealed record TagFilter
{
    public string Key { get; }
    public string Value { get; }

    private TagFilter(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public static Result<TagFilter, string> Create(string key, string value)
    {
        var trimmedKey = key?.Trim() ?? string.Empty;
        if (trimmedKey.Length == 0)
            return Result<TagFilter, string>.Failure("タグキーを入力してください。");

        var trimmedValue = value?.Trim() ?? string.Empty;
        if (trimmedValue.Length == 0)
            return Result<TagFilter, string>.Failure($"タグ「{trimmedKey}」の値を入力してください。");

        return Result<TagFilter, string>.Success(new TagFilter(trimmedKey, trimmedValue));
    }

    public bool IsSatisfiedBy(IReadOnlyDictionary<string, string> tags) =>
        tags.TryGetValue(Key, out var actual) && string.Equals(actual, Value, StringComparison.Ordinal);
}

/// <summary>タグ条件の集合。すべての条件を満たすリソースのみ一致 (AND)。</summary>
public sealed record TagFilters
{
    public static readonly TagFilters Empty = new([]);

    public IReadOnlyList<TagFilter> Items { get; }

    public bool IsEmpty => Items.Count == 0;

    private TagFilters(IReadOnlyList<TagFilter> items) => Items = items;

    public static TagFilters Of(params TagFilter[] filters) => filters.Length == 0 ? Empty : new(filters.ToArray());

    public static TagFilters From(IEnumerable<TagFilter> filters) => Of(filters.ToArray());

    public bool IsSatisfiedBy(IReadOnlyDictionary<string, string> tags) =>
        Items.All(f => f.IsSatisfiedBy(tags));

    public bool Equals(TagFilters? other) =>
        other is not null && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => Items.Count;
}
