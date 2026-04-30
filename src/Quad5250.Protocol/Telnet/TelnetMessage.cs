namespace Quad5250.Protocol.Telnet;

/// <summary>
/// One unit of decoded telnet output produced by <see cref="TelnetParser"/>.
/// </summary>
public abstract record TelnetMessage;

/// <summary>One complete record (terminated by IAC EOR), with IAC IAC unescaped.</summary>
public sealed record TelnetRecord(byte[] Data) : TelnetMessage;

/// <summary>An option negotiation: Command is one of WILL/WONT/DO/DONT.</summary>
public sealed record TelnetNegotiate(byte Command, byte Option) : TelnetMessage;

/// <summary>A subnegotiation block (between IAC SB and IAC SE), with IAC IAC unescaped.</summary>
public sealed record TelnetSubneg(byte Option, byte[] Data) : TelnetMessage;
