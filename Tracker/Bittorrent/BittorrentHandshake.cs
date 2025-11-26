using System.Runtime.InteropServices;

namespace Umi.Dht.Client.Bittorrent;

[StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
public readonly ref struct BittorrentHandshake
{
    public const string HEADER = "BitTorrent protocol";

    public const sbyte PROTOCOL_TYPE = 0x13;

    [field: MarshalAs(UnmanagedType.U1)]
    public sbyte Protocol { get; init; }

    [field:MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16 )]
    public string Header { get; init; }

    [field:MarshalAs(UnmanagedType.ByValArray, ArraySubType =  UnmanagedType.U1, SizeConst = 20)]
    public ReadOnlySpan<byte> PeerId { get; init; }

    [field:MarshalAs(UnmanagedType.ByValArray, ArraySubType =  UnmanagedType.U1, SizeConst = 20)]
    public ReadOnlySpan<byte> InfoHash { get; init; }
}