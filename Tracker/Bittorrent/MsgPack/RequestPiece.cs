namespace Umi.Dht.Client.Bittorrent.MsgPack;

public readonly struct RequestPiece
{
    public required uint Piece { get; init; }

    public required uint Offset { get; init; }

    public required uint Length { get; init; }
}