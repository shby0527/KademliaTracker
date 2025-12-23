namespace Umi.Dht.Client.Protocol;

public interface INodeIdFactory
{
    ReadOnlyMemory<byte> NodeId { get; }
}