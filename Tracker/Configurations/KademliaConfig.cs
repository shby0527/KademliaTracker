namespace Umi.Dht.Client.Configurations;

public class KademliaConfig
{
    public int Port { get; init; } = 13279;

    public IDictionary<string, int> BootstrapList { get; init; } = new Dictionary<string, int>();
}