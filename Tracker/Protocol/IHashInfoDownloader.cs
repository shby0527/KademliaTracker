using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Protocol;

public enum DownloadState : byte
{
    Stopped = 1,
    Downloading,
    Paused,
    Done
}

public sealed class BittorrentProgressEventArgs(long total, long downloaded, IEnumerable<FileListProgress> subFiles)
    : EventArgs
{
    public long TotalLength { get; } = total;
    public long Downloaded { get; } = downloaded;

    public IEnumerable<FileListProgress> FileProgresses { get; } = subFiles;
}

public record FileListProgress(string Path, long TotalLength, long Downloaded);

public interface IHashInfoDownloader : IDisposable, IAsyncDisposable
{
    public DownloadState State { get; }

    public event EventHandler<BittorrentProgressEventArgs> ProgressChanged;

    public event EventHandler DownloadFinished;

    public ref TorrentDirectoryInfo Info { get; }

    Task StartDownloadAsync(CancellationToken token = default);

    Task PauseDownloadAsync(CancellationToken token = default);

    Task ResumeDownloadAsync(CancellationToken token = default);

    Task StopDownloadAsync(CancellationToken token = default);
}