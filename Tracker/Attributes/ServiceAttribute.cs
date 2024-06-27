namespace Umi.Dht.Client.Attributes;

/// <summary>
/// 标记服务的特性
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ServiceAttribute(string name, ServiceScope scoped) : Attribute
{
    public string Name { get; } = name;

    public ServiceScope Scoped { get; } = scoped;

    public ServiceAttribute()
        : this(string.Empty, ServiceScope.Scoped)
    {
    }

    public ServiceAttribute(ServiceScope scoped)
        : this(string.Empty, scoped)
    {
    }

    public IEnumerable<string> Interceptors { get; set; } = [];
}

/// <summary>
/// 服务的注册范围
/// </summary>
public enum ServiceScope : byte
{
    /// <summary>
    /// 单例
    /// </summary>
    Singleton = 1,

    /// <summary>
    /// 原型
    /// </summary>
    Prototype = 2,

    /// <summary>
    /// 每个范围内单例
    /// </summary>
    Scoped = 3
}