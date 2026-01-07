namespace Umi.Dht.Torrent.Protocol.Configurations;

public sealed class TorrentProtocolOptions
{
    public required int Port { get; set; }

    public required bool EnableAuthentication { get; set; }

    public required bool EnableEncryption { get; set; }

    public IDictionary<string, string>? Users { get; set; }
}