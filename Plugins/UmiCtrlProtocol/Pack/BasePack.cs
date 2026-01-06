using System.Runtime.InteropServices;

namespace Umi.Dht.Control.Protocol.Pack;

// ping/pong Length is 0 and no payload 
// this struct total size 4+1+20+8 = 34 bytes
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BasePack
{
    [field: MarshalAs(UnmanagedType.U4)] public required uint Magic { get; init; }

    [field: MarshalAs(UnmanagedType.U1)] public required byte Version { get; init; }

    // 20 bytes
    [field: MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 20)]
    public required byte[] Session { get; init; }

    [field: MarshalAs(UnmanagedType.U1)] public required byte Command { get; init; }

    // payload Length , not includes base fields
    [field: MarshalAs(UnmanagedType.U8)] public required ulong Length { get; init; }

    // payload data

    public ReadOnlySpan<byte> Encode()
    {
        Span<byte> buffer = new byte[Marshal.SizeOf<BasePack>()];
        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                Marshal.StructureToPtr(this, new IntPtr(ptr), false);
            }
        }

        return buffer;
    }

    public static bool Decode(ReadOnlySpan<byte> data, out BasePack result)
    {
        if (data.Length < Marshal.SizeOf<BasePack>()) throw new FormatException("Base Package Length Error");
        unsafe
        {
            fixed (byte* ptr = data)
            {
                result = Marshal.PtrToStructure<BasePack>(new IntPtr(ptr));
                return result.Magic != Constants.MAGIC ? throw new FormatException("Magic Error") : true;
            }
        }
    }
}