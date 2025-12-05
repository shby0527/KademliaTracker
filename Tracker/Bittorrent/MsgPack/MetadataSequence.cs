using System.Buffers;
using System.Diagnostics;

namespace Umi.Dht.Client.Bittorrent.MsgPack;

public sealed class MetadataSequence : ReadOnlySequenceSegment<byte>
{
    private MetadataSequence(ReadOnlyMemory<byte> memory)
    {
        Memory = memory;
        RunningIndex = 0;
    }


    private MetadataSequence Append(ReadOnlyMemory<byte> memory)
    {
        var segment = new MetadataSequence(memory)
        {
            RunningIndex = RunningIndex + Memory.Length
        };
        Next = segment;
        return segment;
    }

    public static ReadOnlySequence<byte> CreateSequenceFromList(IEnumerable<ReadOnlyMemory<byte>> memories)
    {
        Debug.Assert(memories is not null, "linkedList is null");
        using var enumerator = memories.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return ReadOnlySequence<byte>.Empty;
        }

        var first = new MetadataSequence(enumerator.Current);
        var current = first;
        while (enumerator.MoveNext())
        {
            current = current.Append(enumerator.Current);
        }

        return new ReadOnlySequence<byte>(first, 0, current, current.Memory.Length);
    }
}