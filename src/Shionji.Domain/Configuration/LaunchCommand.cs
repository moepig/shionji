using Shionji.Domain.Primitives;

namespace Shionji.Domain.Configuration;

/// <summary>
/// 接続中に実行できるコマンド 1 件。転送設定ごとに任意の数を持つ。
/// コマンド行に含まれるプレースホルダの解釈は、実行する側が行う。
/// </summary>
public sealed record LaunchCommand
{
    /// <summary>表示に使う名前。空の指定ではコマンド行そのものになる。</summary>
    public string Label { get; }

    public string CommandLine { get; }

    private LaunchCommand(string label, string commandLine)
    {
        Label = label;
        CommandLine = commandLine;
    }

    public static Result<LaunchCommand, string> Create(string label, string commandLine)
    {
        var trimmedCommand = commandLine?.Trim() ?? string.Empty;
        if (trimmedCommand.Length == 0)
            return Result<LaunchCommand, string>.Failure("コマンドを入力してください。");

        var trimmedLabel = label?.Trim() ?? string.Empty;
        if (trimmedLabel.Length > 64)
            return Result<LaunchCommand, string>.Failure("コマンドの表示名が長すぎます (64 文字以内)。");

        return Result<LaunchCommand, string>.Success(
            new LaunchCommand(trimmedLabel.Length > 0 ? trimmedLabel : trimmedCommand, trimmedCommand));
    }

    public override string ToString() => CommandLine;
}

/// <summary>転送設定 1 件が持つコマンドの並び。並び順が表示順になる。</summary>
public sealed record LaunchCommands
{
    public static readonly LaunchCommands Empty = new([]);

    public IReadOnlyList<LaunchCommand> Items { get; }

    public bool IsEmpty => Items.Count == 0;

    private LaunchCommands(IReadOnlyList<LaunchCommand> items) => Items = items;

    public static LaunchCommands From(IEnumerable<LaunchCommand> commands)
    {
        var items = commands.ToArray();
        return items.Length == 0 ? Empty : new LaunchCommands(items);
    }

    public bool Equals(LaunchCommands? other) =>
        other is not null && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => Items.Count;
}
