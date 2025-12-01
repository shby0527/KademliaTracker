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

public class MetadataHandshakeEventArg(MetadataPiece metadata) : EventArgs
{
    public MetadataPiece Metadata { get; } = metadata;
}

public delegate void MetadataHandshakeEventHandler(IBittorrentPeer peer, MetadataHandshakeEventArg e);

public delegate void PeerExchangeEventHandler(IBittorrentPeer sender, PeerExchangeEventArg e);

public delegate void MetadataPieceEventHandler(IBittorrentPeer sender, MetadataPieceEventArg e);

public delegate void PeerCloseEventHandler(IPeer sender, PeerCloseEventArg e);