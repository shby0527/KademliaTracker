using System.Runtime.InteropServices;
using System.Text;

namespace Umi.Dht.Client.Bittorrent;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public ref struct BittorrentHandshake
{
    public string Header => "19:BitTorrent protocol";

    public ReadOnlySpan<byte> InfoHash { get; set; }

    public ReadOnlySpan<byte> PeerId { get; set; }

    public ReadOnlySpan<byte> Encode()
    {
        var header = Encoding.UTF8.GetBytes(Header);
        Span<byte> handshake = new byte[header.Length + 8 + PeerId.Length + InfoHash.Length];
        header.CopyTo(handshake);
        InfoHash.CopyTo(handshake[(header.Length + 8)..]);
        PeerId.CopyTo(handshake[(header.Length + 8 + InfoHash.Length)..]);
        return handshake;
    }
}