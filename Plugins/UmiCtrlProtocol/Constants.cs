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

    // length is 0, server will ignore length
    public const byte HANDSHAKE = 0x02;

    public const byte PING = 0x10;

    public const byte PONG = 0x11;

    public const byte CALL = 0xF0;

    public const byte RESPONSE = 0xFA;

    /********************************/
    // handshake error
    public const int HANDSHAKE_ERROR_CODE = 0x10_00_01;
    // require Auth
    public const int REQUIRE_AUTH_ERROR_CODE = 0x10_00_02;
    public const int FAILURE_AUTH_ERROR_CODE = 0x10_00_03;
}