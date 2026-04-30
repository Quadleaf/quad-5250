namespace Quad5250.Protocol.Telnet;

/// <summary>
/// Telnet IAC commands and options used by TN5250/TN5250E.
/// Refs: RFC 854 (telnet), RFC 1572 (NEW-ENVIRON), RFC 2877/4777 (TN5250E).
/// </summary>
public static class TelnetCodes
{
    public const byte IAC = 0xFF;
    public const byte DONT = 0xFE;
    public const byte DO = 0xFD;
    public const byte WONT = 0xFC;
    public const byte WILL = 0xFB;
    public const byte SB = 0xFA;
    public const byte SE = 0xF0;
    public const byte EOR = 0xEF;

    public const byte OptBinary = 0x00;
    public const byte OptEcho = 0x01;
    public const byte OptSuppressGoAhead = 0x03;
    public const byte OptTerminalType = 0x18;
    public const byte OptEndOfRecord = 0x19;
    public const byte OptNewEnviron = 0x27;

    public const byte EnvIs = 0x00;
    public const byte EnvSend = 0x01;
    public const byte EnvInfo = 0x02;
    public const byte EnvVar = 0x00;
    public const byte EnvValue = 0x01;
    public const byte EnvEsc = 0x02;
    public const byte EnvUserVar = 0x03;
}
