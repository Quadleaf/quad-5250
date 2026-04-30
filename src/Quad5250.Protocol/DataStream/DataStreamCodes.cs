namespace Quad5250.Protocol.DataStream;

/// <summary>
/// 5250 datastream command codes. Sent by the host inside a Write command record.
/// Ref: IBM "5250 Functions Reference" SC30-3533.
/// </summary>
public static class Cmd
{
    public const byte WriteToDisplay = 0x11;
    public const byte WriteErrorCode = 0x21;
    public const byte WriteErrorCodeToWindow = 0x22;
    public const byte ReadInputFields = 0x42;
    public const byte ReadMdtFields = 0x52;
    public const byte ReadMdtAlt = 0x53;
    public const byte ReadScreenImmediate = 0x62;
    public const byte ReadImmediate = 0x72;
    public const byte SaveScreen = 0x02;
    public const byte SavePartialScreen = 0x03;
    public const byte RestoreScreen = 0x12;
    public const byte RestorePartialScreen = 0x13;
    public const byte ClearUnit = 0x40;
    public const byte ClearUnitAlternate = 0x20;
    public const byte ClearFormatTable = 0x50;
    public const byte WriteStructuredField = 0xF3;
    public const byte CancelInvite = 0x44;
    public const byte MessageLightOn = 0x23;
    public const byte MessageLightOff = 0x24;
    public const byte Roll = 0x23;
}

/// <summary>
/// 5250 datastream order codes. Embedded inside a Write to Display stream.
/// </summary>
public static class Order
{
    public const byte StartOfHeader = 0x01;     // SOH
    public const byte RepeatToAddress = 0x02;   // RA
    public const byte EraseToAddress = 0x03;    // EA
    public const byte TransparentData = 0x03;
    public const byte SetBufferAddress = 0x11;  // SBA
    public const byte WriteExtendedAttribute = 0x12;
    public const byte InsertCursor = 0x13;      // IC
    public const byte MoveCursor = 0x14;        // MC
    public const byte WriteToDisplayStructuredField = 0x15;
    public const byte StartOfField = 0x1D;      // SF
}

/// <summary>
/// AID (Attention Identifier) bytes returned to the host on a read.
/// </summary>
public enum Aid : byte
{
    None = 0x00,
    F1 = 0x31, F2 = 0x32, F3 = 0x33, F4 = 0x34, F5 = 0x35,
    F6 = 0x36, F7 = 0x37, F8 = 0x38, F9 = 0x39, F10 = 0x3A,
    F11 = 0x3B, F12 = 0x3C,
    F13 = 0xB1, F14 = 0xB2, F15 = 0xB3, F16 = 0xB4, F17 = 0xB5,
    F18 = 0xB6, F19 = 0xB7, F20 = 0xB8, F21 = 0xB9, F22 = 0xBA,
    F23 = 0xBB, F24 = 0xBC,
    Clear = 0xBD,
    Enter = 0xF1,
    Help = 0xF3,
    RollDown = 0xF4,
    RollUp = 0xF5,
    Print = 0xF6,
    RecordBackspace = 0xF8,
    AutoEnter = 0x3F,
}
