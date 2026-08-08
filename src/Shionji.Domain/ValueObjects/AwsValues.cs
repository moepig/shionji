using System.Text.RegularExpressions;
using Shionji.Domain.Primitives;

namespace Shionji.Domain.ValueObjects;

/// <summary>AWS の名前付きプロファイル名。</summary>
public sealed record ProfileName
{
    public string Value { get; }

    private ProfileName(string value) => Value = value;

    public static Result<ProfileName, string> Create(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result<ProfileName, string>.Failure("プロファイル名を入力してください。");
        return Result<ProfileName, string>.Success(new ProfileName(trimmed));
    }

    public override string ToString() => Value;
}

/// <summary>AWS リージョン名 (例: ap-northeast-1)。</summary>
public sealed partial record AwsRegion
{
    public string Value { get; }

    private AwsRegion(string value) => Value = value;

    public static Result<AwsRegion, string> Create(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result<AwsRegion, string>.Failure("リージョンを入力してください。");
        if (!RegionPattern().IsMatch(trimmed))
            return Result<AwsRegion, string>.Failure($"リージョン名の形式が不正です: {trimmed}");
        return Result<AwsRegion, string>.Success(new AwsRegion(trimmed));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex RegionPattern();

    public override string ToString() => Value;
}

/// <summary>AWS API 呼び出しの文脈 (プロファイル + リージョン)。</summary>
public sealed record AwsContext(ProfileName Profile, AwsRegion Region);

/// <summary>EC2 インスタンス ID。</summary>
public sealed partial record InstanceId
{
    public string Value { get; }

    private InstanceId(string value) => Value = value;

    public static Result<InstanceId, string> Create(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!InstanceIdPattern().IsMatch(trimmed))
            return Result<InstanceId, string>.Failure($"インスタンス ID の形式が不正です (例: i-0123456789abcdef0): {trimmed}");
        return Result<InstanceId, string>.Success(new InstanceId(trimmed));
    }

    [GeneratedRegex("^i-[0-9a-f]{8,17}$")]
    private static partial Regex InstanceIdPattern();

    public override string ToString() => Value;
}

/// <summary>ECS クラスター名。</summary>
public sealed record ClusterName
{
    public string Value { get; }

    private ClusterName(string value) => Value = value;

    public static Result<ClusterName, string> Create(string value) =>
        SimpleName.Create(value, "クラスター名").Map(v => new ClusterName(v));

    public override string ToString() => Value;
}

/// <summary>ECS サービス名。</summary>
public sealed record ServiceName
{
    public string Value { get; }

    private ServiceName(string value) => Value = value;

    public static Result<ServiceName, string> Create(string value) =>
        SimpleName.Create(value, "サービス名").Map(v => new ServiceName(v));

    public override string ToString() => Value;
}

/// <summary>ECS コンテナ名。</summary>
public sealed record ContainerName
{
    public string Value { get; }

    private ContainerName(string value) => Value = value;

    public static Result<ContainerName, string> Create(string value) =>
        SimpleName.Create(value, "コンテナ名").Map(v => new ContainerName(v));

    public override string ToString() => Value;
}

/// <summary>解決したリソースの識別子 (システム内部で生成)。</summary>
public sealed record ResourceId
{
    public string Value { get; }

    public ResourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>SSM StartSession の Target に渡す識別子 (システム内部で生成)。</summary>
public sealed record SsmTargetId
{
    public string Value { get; }

    public SsmTargetId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public static SsmTargetId ForInstance(InstanceId instanceId) => new(instanceId.Value);

    public static SsmTargetId ForEcsTask(ClusterName cluster, string taskId, string runtimeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeId);
        return new SsmTargetId($"ecs:{cluster.Value}_{taskId}_{runtimeId}");
    }

    public override string ToString() => Value;
}

file static class SimpleName
{
    public static Result<string, string> Create(string? value, string label)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result<string, string>.Failure($"{label}を入力してください。");
        if (trimmed.Length > 255)
            return Result<string, string>.Failure($"{label}が長すぎます (255 文字以内)。");
        if (trimmed.Any(char.IsWhiteSpace))
            return Result<string, string>.Failure($"{label}に空白は使用できません。");
        return Result<string, string>.Success(trimmed);
    }
}
