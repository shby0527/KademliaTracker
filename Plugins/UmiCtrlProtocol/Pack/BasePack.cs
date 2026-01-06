using System.Runtime.InteropServices;

namespace Umi.Dht.Control.Protocol.Pack;

// ping/pong Length is 0 and no payload 
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BasePack
{
    [field: MarshalAs(UnmanagedType.U4)]
    public required uint Magic { get; init; }

    [field: MarshalAs(UnmanagedType.U1)]
    public byte Version { get; init; }

    // 20 bytes
    [field: MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 20)]
    public ReadOnlyMemory<byte> Session { get; init; }

    [field: MarshalAs(UnmanagedType.U1)]
    public byte Command { get; init; }

    // payload Length , not includes base fields
    [field: MarshalAs(UnmanagedType.U8)]
    public ulong Length { get; init; }

    // payload data
}