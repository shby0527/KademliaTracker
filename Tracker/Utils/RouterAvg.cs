namespace Umi.Dht.Client.Utils;

public record RouterAvg
{
    public required long BucketCount { get; init; }

    public required IDictionary<int[], long> Buckets { get; init; }
}