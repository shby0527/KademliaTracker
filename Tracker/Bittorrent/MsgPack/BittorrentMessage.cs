namespace Umi.Dht.Client.Bittorrent.MsgPack;

public readonly ref struct BittorrentMessage
{
    public const byte EXTENDED = 0x14;
    public const byte CHOKE = 0x00;
    public const byte UNCHOKE = 0x01;
    public const byte INTERESTED = 0x02;
    public const byte NOT_INTERESTED = 0x03;
    public const byte HAVE = 0x04;
    public const byte BITFIELD = 0x05;
    public const byte REQUEST = 0x06;
    public const byte PIECE = 0x07;
    public const byte CANCEL = 0x08;

    public const long PIECE_SIZE = 16384;

    public uint MsgLength { get; init; }

    public byte MsgType { get; init; }


    public const long METADATA_REQUEST = 0;
    public const long METADATA_DATA = 1;
    public const long METADATA_REJECT = 2;
}