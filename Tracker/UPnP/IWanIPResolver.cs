namespace Umi.Dht.Client.UPnP;

public interface IWanIPResolver
{
    public Lazy<string>? ExternalIPAddress { get; }
}