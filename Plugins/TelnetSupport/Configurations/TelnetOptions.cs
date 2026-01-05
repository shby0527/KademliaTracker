namespace Umi.Dht.Client.Telnet.Configurations;

public class TelnetOptions
{
    public int Port { get; set; }

    /// <summary>
    /// User = key, password = value
    /// </summary>
    public IDictionary<string, string> Users { get; set; }

    public int HistoryCount { get; set; }
}