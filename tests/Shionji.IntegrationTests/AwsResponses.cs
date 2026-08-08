namespace Shionji.IntegrationTests;

/// <summary>各サービスの応答本文を組み立てるヘルパー (Query プロトコルは XML、ECS は JSON)。</summary>
public static class AwsResponses
{
    public static StubResponse Ec2Instances(
        IEnumerable<(string Id, string? PrivateIp, string? NameTag)> instances,
        string? nextToken = null)
    {
        var items = string.Join("\n", instances.Select(i => $"""
            <item>
              <instanceId>{i.Id}</instanceId>
              <instanceState><code>16</code><name>running</name></instanceState>
              {(i.PrivateIp is null ? "" : $"<privateIpAddress>{i.PrivateIp}</privateIpAddress>")}
              <tagSet>
                {(i.NameTag is null ? "" : $"<item><key>Name</key><value>{i.NameTag}</value></item>")}
              </tagSet>
            </item>
            """));

        return StubResponse.Xml($"""
            <DescribeInstancesResponse xmlns="http://ec2.amazonaws.com/doc/2016-11-15/">
              <requestId>req-1</requestId>
              <reservationSet>
                <item>
                  <reservationId>r-1</reservationId>
                  <instancesSet>{items}</instancesSet>
                </item>
              </reservationSet>
              {(nextToken is null ? "" : $"<nextToken>{nextToken}</nextToken>")}
            </DescribeInstancesResponse>
            """);
    }

    public static StubResponse AuroraClusters(
        IEnumerable<(string Id, string Writer, string Reader, int Port, (string Key, string Value)[] Tags)> clusters)
    {
        var items = string.Join("\n", clusters.Select(c => $"""
            <DBCluster>
              <DBClusterIdentifier>{c.Id}</DBClusterIdentifier>
              <DBClusterArn>arn:aws:rds:ap-northeast-1:123456789012:cluster:{c.Id}</DBClusterArn>
              <Endpoint>{c.Writer}</Endpoint>
              <ReaderEndpoint>{c.Reader}</ReaderEndpoint>
              <Port>{c.Port}</Port>
              <TagList>
                {string.Join("\n", c.Tags.Select(t => $"<Tag><Key>{t.Key}</Key><Value>{t.Value}</Value></Tag>"))}
              </TagList>
            </DBCluster>
            """));

        return StubResponse.Xml($"""
            <DescribeDBClustersResponse xmlns="http://rds.amazonaws.com/doc/2014-10-31/">
              <DescribeDBClustersResult>
                <DBClusters>{items}</DBClusters>
              </DescribeDBClustersResult>
            </DescribeDBClustersResponse>
            """);
    }

    public static StubResponse ElastiCacheGroups(
        IEnumerable<(string Id, string Primary, string Reader, int Port)> groups)
    {
        var items = string.Join("\n", groups.Select(g => $"""
            <ReplicationGroup>
              <ReplicationGroupId>{g.Id}</ReplicationGroupId>
              <ARN>arn:aws:elasticache:ap-northeast-1:123456789012:replicationgroup:{g.Id}</ARN>
              <NodeGroups>
                <NodeGroup>
                  <NodeGroupId>0001</NodeGroupId>
                  <PrimaryEndpoint><Address>{g.Primary}</Address><Port>{g.Port}</Port></PrimaryEndpoint>
                  <ReaderEndpoint><Address>{g.Reader}</Address><Port>{g.Port}</Port></ReaderEndpoint>
                </NodeGroup>
              </NodeGroups>
            </ReplicationGroup>
            """));

        return StubResponse.Xml($"""
            <DescribeReplicationGroupsResponse xmlns="http://elasticache.amazonaws.com/doc/2015-02-02/">
              <DescribeReplicationGroupsResult>
                <ReplicationGroups>{items}</ReplicationGroups>
              </DescribeReplicationGroupsResult>
            </DescribeReplicationGroupsResponse>
            """);
    }

    public static StubResponse ElastiCacheTags(params (string Key, string Value)[] tags)
    {
        var items = string.Join("\n", tags.Select(t => $"<Tag><Key>{t.Key}</Key><Value>{t.Value}</Value></Tag>"));
        return StubResponse.Xml($"""
            <ListTagsForResourceResponse xmlns="http://elasticache.amazonaws.com/doc/2015-02-02/">
              <ListTagsForResourceResult>
                <TagList>{items}</TagList>
              </ListTagsForResourceResult>
            </ListTagsForResourceResponse>
            """);
    }

    public static StubResponse EcsTaskArns(params string[] arns) =>
        StubResponse.Json(new { taskArns = arns });

    public static StubResponse EcsTasks(
        IEnumerable<(string Arn, string ContainerName, string? RuntimeId, string? PrivateIp)> tasks) =>
        StubResponse.Json(new
        {
            tasks = tasks.Select(t => new
            {
                taskArn = t.Arn,
                containers = new[]
                {
                    new
                    {
                        name = t.ContainerName,
                        runtimeId = t.RuntimeId,
                        networkInterfaces = t.PrivateIp is null
                            ? []
                            : new[] { new { privateIpv4Address = t.PrivateIp } },
                    },
                },
            }).ToArray(),
        });
}
