namespace Umi.Dht.Control.Protocol;

/// <summary>
/// 系统相关常量
/// </summary>
public static class Constants
{
    /// <summary>
    /// 相关魔数
    /// </summary>
    public const uint MAGIC = 0x6C_28_45_FB;

    public const byte VERSION = 0x01;

    /**************************************************/
    // CMD
    public const byte AUTH = 0x01;

    public const byte AUTH_RESPONSE = 0x02;

    public const byte PING = 0x10;

    public const byte PONG = 0x11;

    public const byte CALL = 0xF0;

    public const byte RESPONSE = 0xFA;
}