using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Torrent.Protocol.Configurations;

namespace Umi.Dht.Torrent.Protocol;

[StartUp]
public class StartUp
{
    // Auto Invoke in Main Program
    // ReSharper disable once UnusedMember.Global
    [SuppressMessage("Performance", "CA1822:将成员标记为 static")]
    public void ConfigurationServices(HostBuilderContext ctx, IServiceCollection services)
    {
        services.Configure<TorrentProtocolOptions>(ctx.Configuration.GetSection("TorrentProtocol"));
    }
}