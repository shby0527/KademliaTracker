namespace Umi.Dht.Client.Attributes;

/// <summary>
/// 标记特性，表示用以IoC启动
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StartUpAttribute : Attribute
{
}