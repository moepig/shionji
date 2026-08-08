using Shionji.Domain.ValueObjects;

namespace Shionji.Domain.Configuration;

/// <summary>SSM セッションを張る踏み台の指定。</summary>
public abstract record GatewaySpec
{
    private GatewaySpec() { }

    /// <summary>転送先リソース自身に SSM セッションを張る (EC2 / ECS 転送先のみ)。</summary>
    public sealed record Direct : GatewaySpec
    {
        public static readonly Direct Instance = new();
    }

    /// <summary>EC2 インスタンスを踏み台にする。</summary>
    public sealed record Ec2(Ec2Selector Selector) : GatewaySpec;

    /// <summary>ECS タスク (ECS Exec) を踏み台にする。</summary>
    public sealed record Ecs(ClusterName Cluster, ServiceName Service, ContainerName? Container) : GatewaySpec;
}

/// <summary>踏み台 EC2 インスタンスの特定方法。</summary>
public abstract record Ec2Selector
{
    private Ec2Selector() { }

    /// <summary>インスタンス ID の直接指定。</summary>
    public sealed record ById(InstanceId Id) : Ec2Selector;

    /// <summary>検索条件による自動特定。</summary>
    public sealed record ByQuery(Ec2Query Query) : Ec2Selector;
}
