using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Attributes;

namespace Umi.Dht.Client.Protocol;

[Service(ServiceScope.Singleton)]
public sealed class NodeIdFactory(ILogger<NodeIdFactory> logger, IHostEnvironment environment) : INodeIdFactory
{
    private readonly Lock _lock = new();

    private const string NodeCacheName = ".node.id";

    private static readonly RandomNumberGenerator Rnd = RandomNumberGenerator.Create();

    public ReadOnlyMemory<byte> NodeId
    {
        get
        {
            Memory<byte> id = new byte[20];
            var info = environment.ContentRootFileProvider.GetFileInfo(NodeCacheName);
            if (info is { Exists: true, IsDirectory: false })
            {
                logger.LogTrace("found {f} exists, read it", NodeCacheName);
                using var s = info.CreateReadStream();
                s.ReadExactly(id.Span);
                return id;
            }

            if (info.IsDirectory)
            {
                logger.LogWarning("{f} is a directory", NodeCacheName);
                throw new InvalidOperationException("caching file is a directory");
            }

            lock (_lock)
            {
                logger.LogTrace("in lock scope {f} read", NodeCacheName);
                FileInfo fileInfo = new(Path.Combine(environment.ContentRootPath, NodeCacheName));
                if (fileInfo.Exists)
                {
                    using var f = fileInfo.OpenRead();
                    f.ReadExactly(id.Span);
                    return id;
                }

                Rnd.GetBytes(id.Span);
                using var wf = fileInfo.Create();
                wf.Write(id.Span);
                wf.Flush();
                wf.Close();
                return id;
            }
        }
    }
}