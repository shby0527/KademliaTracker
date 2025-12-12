using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;
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
    IHttpClientFactory httpClientFactory) : BackgroundService, IWanIPResolver
{
    private const int MAX_PACK_SIZE = 0x10000;

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    private readonly SocketAsyncEventArgs _socketAsyncEventArgs = new();

    private readonly ConcurrentQueue<Func<Task>> _actions = new();

    private const string GATEWAY_DEVICE = "InternetGatewayDevice:1";

    private const string WAN_DEVICE = "WANDevice:1";

    private const string WAN_CONNECTION_DEVICE = "WANConnectionDevice:1";

    private const string WAN_IP_CONNECTION = "WANIPConnection:1";

    public Lazy<string>? ExternalIPAddress { get; private set; }

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
        _socketAsyncEventArgs.UserToken = new IPEndPoint(local, 0);
        if (!_socket.ReceiveFromAsync(_socketAsyncEventArgs))
        {
            this.OnReceivedData(_socket, _socketAsyncEventArgs);
        }

        const string discoverPackage =
            "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 3\r\nST: upnp:rootdevice\r\n\r\n";
        logger.LogTrace("sending discover package {p}", discoverPackage);
        var sendByte = _socket.SendTo(Encoding.UTF8.GetBytes(discoverPackage), remoteGroup);
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

        logger.LogTrace("request remote is {remote}", args.RemoteEndPoint);
        var content = args.MemoryBuffer[..args.BytesTransferred];
        var result = Encoding.UTF8.GetString(content.Span);
        logger.LogTrace("result is: {result},", result);
        if (UPnPResponse.TryParse(result, out var response))
        {
            logger.LogTrace("upnp status code {code}", response.Code);
            if (response.Code == HttpStatusCode.OK)
            {
                logger.LogInformation("found upnp device response from server {server}",
                    response.Headers.GetValues("SERVER")?[0] ?? "");
                // getLocation
                var values = response.Headers.GetValues("LOCATION");
                if (values != null)
                {
                    this.TakeUPnPXml(values[0], args.RemoteEndPoint, ((IPEndPoint)args.UserToken!).Address)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .OnCompleted(() => logger.LogTrace("action finished"));
                }
            }
        }

        // begin next
        logger.LogTrace("next receive from {ip}", _socketAsyncEventArgs.UserToken);
        _socketAsyncEventArgs.RemoteEndPoint = (IPEndPoint)_socketAsyncEventArgs.UserToken!;
        if (!_socket.ReceiveFromAsync(_socketAsyncEventArgs))
        {
            this.OnReceivedData(_socket, _socketAsyncEventArgs);
        }
    }


    private async Task TakeUPnPXml(string location, EndPoint? remote, IPAddress local)
    {
        logger.LogTrace("take xml from {location}", location);
        using var client = httpClientFactory.CreateClient("default");
        using var response = await client.GetAsync(location);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("response error {code}", response.StatusCode);
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        // var body = await File.ReadAllTextAsync(@"C:\Users\XY4\Desktop\igd.xml");
        logger.LogTrace("remote {r},server response body {body}", remote, body);
        XmlDocument document = new();
        document.LoadXml(body);
        // find root device and find operator node
        // find gateway device
        XmlNamespaceManager manager = new(document.NameTable);
        var ns = document["root"]?.NamespaceURI ?? "urn:schemas-upnp-org:device-1-0";
        manager.AddNamespace("gt", ns);
        try
        {
            var gatewayNode = document.SelectSingleNode(
                $"child::*/gt:device[contains(gt:deviceType, '{GATEWAY_DEVICE}')]",
                manager);
            if (gatewayNode == null)
            {
                logger.LogTrace("can not found {gateway} node", GATEWAY_DEVICE);
                return;
            }

            var wanDevice = gatewayNode.SelectSingleNode(
                $"child::*/gt:device[contains(gt:deviceType,'{WAN_DEVICE}')]",
                manager);
            if (wanDevice == null)
            {
                logger.LogError("can not found {wanDevice} node", WAN_DEVICE);
                return;
            }

            var wanConnectionDevice = wanDevice.SelectSingleNode(
                $"child::*/gt:device[contains(gt:deviceType, '{WAN_CONNECTION_DEVICE}')]",
                manager);
            if (wanConnectionDevice == null)
            {
                logger.LogError("can not found {wanConnectionDevice} node", WAN_CONNECTION_DEVICE);
                return;
            }

            var wanIpConnection = wanConnectionDevice.SelectSingleNode(
                $"child::*/gt:service[contains(gt:serviceType, '{WAN_IP_CONNECTION}')]",
                manager);
            if (wanIpConnection == null)
            {
                logger.LogError("can not found {wanIpConnection} node", WAN_IP_CONNECTION);
                return;
            }

            var controlUrl = wanIpConnection["controlURL"]?.InnerText ?? "";
            if (string.IsNullOrEmpty(controlUrl))
            {
                logger.LogError("can not found controlUrl, in ip connection {node}", wanIpConnection);
                return;
            }

            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
            {
                logger.LogError("error parse location {location}", location);
                return;
            }

            var uriBuilder = new UriBuilder(uri.Scheme, uri.Host, uri.Port)
            {
                Path = controlUrl
            };

            logger.LogTrace("control url is {url}", uriBuilder.Uri);

            ExternalIPAddress = new Lazy<string>(() =>
                GetExternalIPAddress(uriBuilder.Uri, wanIpConnection["serviceType"]!.InnerText, local)
                    .ConfigureAwait(false).GetAwaiter().GetResult());

            await AddPortMapping(uriBuilder.Uri, wanIpConnection["serviceType"]!.InnerText, local);
        }
        catch (Exception e)
        {
            logger.LogError(e, "parsing node error");
        }
    }


    private async Task<string> GetExternalIPAddress(Uri uri, string domain, IPAddress local)
    {
        logger.LogTrace("begin get wan ip address");
        try
        {
            XmlDocument document = new();
            var bodyNode = WarpSoapPackage(document);
            var getExternalIpAddress = document.CreateElement("m", "GetExternalIPAddress", domain);
            bodyNode.AppendChild(getExternalIpAddress);
            var xml = await ConvertXmlString(document);
            logger.LogTrace("send to body {xml}", xml);
            var client = httpClientFactory.CreateClient();
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, uri);
            using var requestContent = new StringContent(xml, Encoding.UTF8, "text/xml");
            requestContent.Headers.Add("SOAPAction", $"{domain}#GetExternalIPAddress");
            postRequest.Content = requestContent;
            postRequest.Headers.Add("Connection", "close");
            postRequest.Headers.Add("Cache-Control", "no-cache");
            postRequest.Headers.Add("Pragma", "no-cache");
            using var response = await client.SendAsync(postRequest);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("error mapping port, status {s}", response.StatusCode);
                return string.Empty;
            }

            var result = await response.Content.ReadAsStringAsync();
            logger.LogTrace("add port mapping result: {r}", result);
            XmlDocument resultDoc = new();
            resultDoc.LoadXml(result);

            return resultDoc?.DocumentElement?.FirstChild?.FirstChild?.FirstChild?.InnerText ?? "";
        }
        catch (Exception e)
        {
            logger.LogError(e, "error Getting wan ip address");
            return string.Empty;
        }
    }


    private async Task AddPortMapping(Uri uri, string domain, IPAddress local)
    {
        try
        {
            logger.LogTrace("add port mapping, domain: {domain}", domain);
            XmlDocument document = new();
            var bodyNode = WarpSoapPackage(document);
            // create add porting mapping request body
            var addPortMappingBody = document.CreateElement("m", "AddPortMapping", domain);
            bodyNode.AppendChild(addPortMappingBody);
            // children
            var nep = document.CreateElement("NewExternalPort");
            nep.InnerText = kademliaConfig.Value.Port.ToString();
            addPortMappingBody.AppendChild(nep);

            var nip = document.CreateElement("NewInternalPort");
            nip.InnerText = kademliaConfig.Value.Port.ToString();
            addPortMappingBody.AppendChild(nip);
            var protocol = document.CreateElement("NewProtocol");
            protocol.InnerText = "UDP";
            addPortMappingBody.AppendChild(protocol);
            var enabled = document.CreateElement("NewEnabled");
            enabled.InnerText = "1";
            addPortMappingBody.AppendChild(enabled);
            var nic = document.CreateElement("NewInternalClient");
            nic.InnerText = local.ToString();
            addPortMappingBody.AppendChild(nic);
            var nld = document.CreateElement("NewLeaseDuration");
            nld.InnerText = "0";
            addPortMappingBody.AppendChild(nld);
            var description = document.CreateElement("NewPortMappingDescription");
            description.InnerText = "UmiKademliaTracker";
            addPortMappingBody.AppendChild(description);
            var nrh = document.CreateElement("NewRemoteHost");
            addPortMappingBody.AppendChild(nrh);
            var xml = await ConvertXmlString(document);
            logger.LogTrace("send to body {xml}", xml);
            var client = httpClientFactory.CreateClient();
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, uri);
            using var requestContent = new StringContent(xml, Encoding.UTF8, "text/xml");
            requestContent.Headers.Add("SOAPAction", $"{domain}#AddPortMapping");
            postRequest.Content = requestContent;
            postRequest.Headers.Add("Connection", "close");
            postRequest.Headers.Add("Cache-Control", "no-cache");
            postRequest.Headers.Add("Pragma", "no-cache");
            using var response = await client.SendAsync(postRequest);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("error mapping port, status {s}", response.StatusCode);
                return;
            }

            var result = await response.Content.ReadAsStringAsync();
            logger.LogTrace("add port mapping result: {r}", result);
            _actions.Enqueue(() => RemovePortMapping(uri, domain, local));
        }
        catch (Exception e)
        {
            logger.LogError(e, "error add port mapping");
        }
    }


    private static async Task<string> ConvertXmlString(XmlDocument document)
    {
        await using var ms = new MemoryStream();
        await using var writer = XmlWriter.Create(new StreamWriter(ms, Encoding.UTF8), new XmlWriterSettings
        {
            Async = true,
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        });
        document.Save(writer);
        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        var xml = await reader.ReadToEndAsync();
        return xml;
    }

    private static XmlElement WarpSoapPackage(XmlDocument document)
    {
        const string ns = "http://schemas.xmlsoap.org/soap/envelope/";
        var envelope = document.CreateElement("s", "Envelope", ns);
        var attribute = document.CreateAttribute("s", "encodingStyle", ns);
        attribute.Value = "http://schemas.xmlsoap.org/soap/encoding/";
        envelope.Attributes.Append(attribute);
        document.AppendChild(envelope);
        var body = document.CreateElement("s", "Body", ns);
        envelope.AppendChild(body);
        return body;
    }

    private async Task RemovePortMapping(Uri uri, string domain, IPAddress local)
    {
        logger.LogTrace("remove port mapping, domain: {domain}", domain);
        try
        {
            XmlDocument document = new();
            var body = WarpSoapPackage(document);
            var deletePortMapping = document.CreateElement("m", "DeletePortMapping", domain);
            body.AppendChild(deletePortMapping);
            var nrh = document.CreateElement("NewRemoteHost");
            deletePortMapping.AppendChild(nrh);
            var nep = document.CreateElement("NewExternalPort");
            nep.InnerText = kademliaConfig.Value.Port.ToString();
            deletePortMapping.AppendChild(nep);
            var protocol = document.CreateElement("NewProtocol");
            protocol.InnerText = "UDP";
            deletePortMapping.AppendChild(protocol);
            var xml = await ConvertXmlString(document);
            logger.LogTrace("send to body {xml}", xml);
            var client = httpClientFactory.CreateClient();
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, uri);
            using var requestContent = new StringContent(xml, Encoding.UTF8, "text/xml");
            requestContent.Headers.Add("SOAPAction", $"{domain}#DeletePortMapping");
            postRequest.Content = requestContent;
            postRequest.Headers.Add("Connection", "close");
            postRequest.Headers.Add("Cache-Control", "no-cache");
            postRequest.Headers.Add("Pragma", "no-cache");
            using var response = await client.SendAsync(postRequest);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("error mapping port, status {s}", response.StatusCode);
                return;
            }

            var result = await response.Content.ReadAsStringAsync();
            logger.LogTrace("delete port mapping result: {r}", result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "remove port error");
        }
    }


    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _socket.Close();
        _socket.Dispose();
        _socketAsyncEventArgs.Completed -= this.OnReceivedData;
        _socketAsyncEventArgs.Dispose();
        logger.LogTrace("release all resource, when discard port mapping");
        List<Task> tasks = [];
        while (!_actions.IsEmpty)
        {
            if (_actions.TryDequeue(out var action))
            {
                tasks.Add(action());
            }
        }

        Task.WaitAll(tasks.ToArray(), cancellationToken);

        return base.StopAsync(cancellationToken);
    }
}