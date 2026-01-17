using System.Net;
using Umi.Dht.Client.Bittorrent.MsgPack;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Bittorrent;

public interface IBittorrentPeer : IPeer, IEquatable<IBittorrentPeer>, IEquatable<IPeer>, IDisposable
{
    string Id { get; }

    ReadOnlyMemory<byte> PeerId { get; }

    bool IsConnected { get; }

    Task Connect();

    Task Disconnect();

    long MetadataLenght { get; }

    Task GetHashMetadata(long piece);

    Task PeerInterested(bool interested);

    Task HavePiece(uint piece);

    Task BitField(ReadOnlyMemory<byte> data);

    Task Choke(bool choke);

    Task Request(RequestPiece piece);

    Task Cancel(RequestPiece piece);

    Task SendData(ReadOnlyMemory<byte> data, uint piece, uint offset);

    event ExtensionHandshakeEventHandler ExtensionHandshake;

    event PeerExchangeEventHandler PeerExchange;

    event PeerCloseEventHandler PeerClose;

    event MetadataPieceEventHandler MetadataPiece;

    event PeerPieceDataEventHandler PeerPieceData;

    event PeerBitFieldEventHandler PeerBitField;

    event PeerHavePieceEventHandler PeerHavePiece;
}

public interface IPeer
{
    NodeInfo Node { get; }

    IPAddress Address { get; }

    int Port { get; }

    byte Flags { get; }
}