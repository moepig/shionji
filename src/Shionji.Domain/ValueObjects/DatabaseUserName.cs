using Shionji.Domain.Primitives;

namespace Shionji.Domain.ValueObjects;

/// <summary>IAM データベース認証で使用する DB ユーザー名。大文字と小文字を保持する。</summary>
public sealed record DatabaseUserName
{
    public string Value { get; }

    private DatabaseUserName(string value) => Value = value;

    public static Result<DatabaseUserName, string> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<DatabaseUserName, string>.Failure("DB ユーザー名を入力してください。");
        if (value.Any(char.IsControl))
            return Result<DatabaseUserName, string>.Failure("DB ユーザー名に制御文字は使えません。");
        return Result<DatabaseUserName, string>.Success(new DatabaseUserName(value.Trim()));
    }

    public override string ToString() => Value;
}
