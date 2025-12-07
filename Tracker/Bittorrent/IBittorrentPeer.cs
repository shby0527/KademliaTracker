using System.Net;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Bittorrent;

public interface IBittorrentPeer : IPeer, IEquatable<IBittorrentPeer>, IEquatable<IPeer>, IDisposable
{
    bool IsConnected { get; }

    Task Connect();

    Task Disconnect();

    MetadataPiece Metadata { get; }

    Task GetHashMetadata(long piece);

    event ExtensionHandshakeEventHandler ExtensionHandshake;

    event PeerExchangeEventHandler PeerExchange;

    event PeerCloseEventHandler PeerClose;

    event MetadataPieceEventHandler MetadataPiece;
}

public interface IPeer
{
    NodeInfo Node { get; }

    IPAddress Address { get; }

    int Port { get; }
}