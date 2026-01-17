using Umi.Dht.Client.Protocol;

namespace Umi.Dht.Client.Bittorrent;

public interface IDownloadProgressPublisher
{
    void OnBegin(ReadOnlyMemory<byte> btih);

    void OnPause(ReadOnlyMemory<byte> btih);

    void OnResume(ReadOnlyMemory<byte> btih);

    void OnProgress(ReadOnlyMemory<byte> btih, BittorrentProgressEventArgs eventArgs);

    void OnFinish(ReadOnlyMemory<byte> btih);

    void OnCancel(ReadOnlyMemory<byte> btih);
}