namespace Umi.Dht.Client.Telnet.Configurations;

public class TelnetOptions
{
    public int Port { get; set; }

    /// <summary>
    /// User = key, password = value
    /// </summary>
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 'required' 修饰符或声明为可以为 null。
    // ReSharper disable once CollectionNeverUpdated.Global
    public IDictionary<string, string> Users { get; set; }
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 'required' 修饰符或声明为可以为 null。

    public int HistoryCount { get; set; }
}