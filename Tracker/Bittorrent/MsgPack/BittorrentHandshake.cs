using System.Runtime.InteropServices;
using System.Text;

namespace Umi.Dht.Client.Bittorrent.MsgPack;

public readonly ref struct BittorrentHandshake
{
    public const string HEADER = "BitTorrent protocol";

    public const byte PROTOCOL_TYPE = 0x13;

    public required byte Protocol { get; init; }

    public required string Header { get; init; }

    public required ReadOnlySpan<byte> Reserve { get; init; }

    public required ReadOnlySpan<byte> InfoHash { get; init; }

    public required ReadOnlySpan<byte> PeerId { get; init; }


    /// <summary>
    ///  创建协议封包
    /// </summary>
    /// <returns></returns>
    public ReadOnlySpan<byte> Encode()
    {
        Span<byte> b = new byte[68];
        b[0] = PROTOCOL_TYPE;
        Encoding.ASCII.GetBytes(HEADER, b[1..20]);
        Reserve.CopyTo(b[20..28]);
        InfoHash.CopyTo(b[28..48]);
        PeerId.CopyTo(b[48..]);
        return b;
    }
}