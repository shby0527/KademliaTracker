using System.Runtime.InteropServices;
using System.Text;

namespace Umi.Dht.Client.Bittorrent;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly ref struct BittorrentHandshake
{
    private const string Header = "BitTorrent protocol";

    public ReadOnlySpan<byte> InfoHash { get; init; }

    public ReadOnlySpan<byte> PeerId { get; init; }

    public ReadOnlySpan<byte> Encode()
    {
        var header = Encoding.UTF8.GetBytes(Header);
        Span<byte> handshake = new byte[1 + header.Length + 8 + PeerId.Length + InfoHash.Length];
        handshake[0] = 19;
        InfoHash.CopyTo(handshake[(header.Length + 9)..]);
        PeerId.CopyTo(handshake[(header.Length + 9 + InfoHash.Length)..]);
        return handshake;
    }
}