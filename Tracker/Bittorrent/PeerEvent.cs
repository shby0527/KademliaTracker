using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Bittorrent;

/// <summary>
/// peer exchange Event Argument
/// </summary>
/// <param name="add">added peer</param>
/// <param name="remove">removed peer </param>
public class PeerExchangeEventArg(IEnumerable<IPeer> add, IEnumerable<IPeer> remove) : EventArgs
{
    public IEnumerable<IPeer> Add { get; } = add;

    public IEnumerable<IPeer> Remove { get; } = remove;
}

/// <summary>
/// metadata 分片
/// </summary>
/// <param name="buffer">分片数据</param>
/// <param name="piece">分片号</param>
public class MetadataPieceEventArg(ReadOnlyMemory<byte> buffer, long piece, long msgType, long length) : EventArgs
{
    public ReadOnlyMemory<byte> Buffer { get; } = buffer;

    public long Piece { get; } = piece;

    public long MsgType { get; } = msgType;

    public long Length { get; } = length;
}

public class PeerCloseEventArg(int reason, bool remove) : EventArgs
{
    public bool Remove { get; } = remove;
    public int Reason { get; } = reason;
}

public class ExtensionHandshake(bool hasMetadataAttr, bool hasPeerExchange, long metadataLength) : EventArgs
{
    public bool HasMetadataAttr { get; } = hasMetadataAttr;
    public long MetadataLength { get; } = metadataLength;
    public bool HasPeerExchange { get; } = hasPeerExchange;
}

public class PeerHavePieceEventArg(uint piece) : EventArgs
{
    public uint Piece { get; } = piece;
}

public class PeerBitFieldEventArg(ReadOnlyMemory<byte> bitfield) : EventArgs
{
    public ReadOnlyMemory<byte> Bitfield { get; } = bitfield;
}

public class PeerPieceDataEventArg(uint piece, uint offset, ReadOnlyMemory<byte> pieceData) : EventArgs
{
    public ReadOnlyMemory<byte> PieceData { get; } = pieceData;
    public uint Piece { get; } = piece;
    public uint Offset { get; } = offset;
}

public delegate void ExtensionHandshakeEventHandler(IBittorrentPeer peer, ExtensionHandshake e);

public delegate void PeerExchangeEventHandler(IBittorrentPeer sender, PeerExchangeEventArg e);

public delegate void MetadataPieceEventHandler(IBittorrentPeer sender, MetadataPieceEventArg e);

public delegate void PeerCloseEventHandler(IBittorrentPeer sender, PeerCloseEventArg e);

public delegate void PeerHavePieceEventHandler(IBittorrentPeer sender, PeerHavePieceEventArg e);

public delegate void PeerBitFieldEventHandler(IBittorrentPeer sender, PeerBitFieldEventArg e);

public delegate void PeerPieceDataEventHandler(IBittorrentPeer sender, PeerPieceDataEventArg e);