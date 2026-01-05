using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.Telnet.Configurations;

namespace Umi.Dht.Client.Telnet;

[StartUp]
public class StartUp
{
    // Auto Invoke in Main Program
    // ReSharper disable once UnusedMember.Global
    [SuppressMessage("Performance", "CA1822:将成员标记为 static")]
    public void ConfigurationServices(HostBuilderContext ctx, IServiceCollection services)
    {
        services.Configure<TelnetOptions>(ctx.Configuration.GetSection("Telnet"));
    }
}