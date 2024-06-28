using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.Configurations;
using Umi.Dht.Client.UPnP;

namespace Umi.Dht.Client.Workers;

[Service(ServiceScope.Singleton)]
public class UpnpDiscoverWorker(
    ILogger<UpnpDiscoverWorker> logger,
    IOptions<KademliaConfig> kademliaConfig,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : BackgroundService
{
    private const int MAX_PACK_SIZE = 0x10000;

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    private readonly SocketAsyncEventArgs _socketAsyncEventArgs = new();

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogTrace("begin discover UPnP Device");
        Memory<byte> buffer = new byte[MAX_PACK_SIZE];
        var remoteGroup = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        var cla = configuration.GetValue<string>("LocalAddress");
        var local = string.IsNullOrEmpty(cla) ? GetLocalAddress() : IPAddress.Parse(cla);
        logger.LogTrace("found local ip address is {ip}", local);
        MulticastOption option = new(remoteGroup.Address, local);
        _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, option);
        _socket.Bind(new IPEndPoint(local, 0));
        _socketAsyncEventArgs.SetBuffer(buffer);
        _socketAsyncEventArgs.Completed += this.OnReceivedData;
        _socketAsyncEventArgs.RemoteEndPoint = new IPEndPoint(local, 0);
        if (!_socket.ReceiveFromAsync(_socketAsyncEventArgs))
        {
            this.OnReceivedData(_socket, _socketAsyncEventArgs);
        }

        const string discoverPackage =
            "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN:\"ssdp:discover\"\r\nMX:3\r\nST:upnp:rootdevice\r\n";
        logger.LogTrace("sending discover package {p}", discoverPackage);
        var sendByte = _socket.SendTo(Encoding.ASCII.GetBytes(discoverPackage), remoteGroup);
        logger.LogTrace("send bytes {s}", sendByte);
        return Task.CompletedTask;
    }


    private static IPAddress GetLocalAddress()
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return IPAddress.Loopback;
        }

        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        foreach (var nif in interfaces)
        {
            if (nif is not
                {
                    NetworkInterfaceType: NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211,
                    SupportsMulticast: true,
                    OperationalStatus: OperationalStatus.Up
                }) continue;
            var ipProperties = nif.GetIPProperties();
            var addresses = ipProperties.UnicastAddresses;
            foreach (var address in addresses)
            {
                var ip = address.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                // 找到一个就行了
                return ip;
            }
        }

        return IPAddress.Any;
    }


    private void OnReceivedData(object? sender, SocketAsyncEventArgs args)
    {
        if (args is not
            {
                SocketError: SocketError.Success,
                LastOperation: SocketAsyncOperation.ReceiveFrom,
                BytesTransferred: > 0
            })
        {
            logger.LogTrace("error receive");
            return;
        }

        var content = args.MemoryBuffer[..args.BytesTransferred];
        var result = Encoding.ASCII.GetString(content.Span);
        logger.LogTrace("result is: {result}", result);
        if (UPnPResponse.TryParse(result, out var response))
        {
            logger.LogTrace("upnp status code {code}", response!.Code);
            if (response!.Code == HttpStatusCode.OK)
            {
                logger.LogInformation("found upnp device response from server {server}",
                    response!.Headers.GetValues("SERVER")?[0] ?? "");
                // getLocation
                var values = response!.Headers.GetValues("LOCATION");
                if (values != null)
                {
                    this.TakeUPnPXml(values[0])
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .OnCompleted(() => logger.LogTrace("action finished"));
                }
            }
        }

        // begin next
        if (!_socket.ReceiveFromAsync(_socketAsyncEventArgs))
        {
            this.OnReceivedData(_socket, _socketAsyncEventArgs);
        }
    }


    private async Task TakeUPnPXml(string location)
    {
        logger.LogTrace("take xml from {location}", location);
        using var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(location);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("response error {code}", response.StatusCode);
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        logger.LogTrace("server response body {body}", body);
    }


    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _socket.Close();
        _socket.Dispose();
        _socketAsyncEventArgs.Completed -= this.OnReceivedData;
        _socketAsyncEventArgs.Dispose();
        return base.StopAsync(cancellationToken);
    }
}