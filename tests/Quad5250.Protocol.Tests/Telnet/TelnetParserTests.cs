using Quad5250.Protocol.Telnet;
using Xunit;

namespace Quad5250.Protocol.Tests.Telnet;

public class TelnetParserTests
{
    private const byte IAC = TelnetCodes.IAC;
    private const byte DO = TelnetCodes.DO;
    private const byte WILL = TelnetCodes.WILL;
    private const byte SB = TelnetCodes.SB;
    private const byte SE = TelnetCodes.SE;
    private const byte EOR = TelnetCodes.EOR;
    private const byte TT = TelnetCodes.OptTerminalType;
    private const byte BIN = TelnetCodes.OptBinary;

    [Fact]
    public void PlainBytes_BecomeRecord_OnEor()
    {
        var p = new TelnetParser();
        var msgs = p.Feed(new byte[] { 0x01, 0x02, 0x03, IAC, EOR });

        var rec = Assert.IsType<TelnetRecord>(Assert.Single(msgs));
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, rec.Data);
    }

    [Fact]
    public void IacIac_IsLiteral_0xFF_InRecord()
    {
        var p = new TelnetParser();
        var msgs = p.Feed(new byte[] { 0xAA, IAC, IAC, 0xBB, IAC, EOR });

        var rec = Assert.IsType<TelnetRecord>(Assert.Single(msgs));
        Assert.Equal(new byte[] { 0xAA, 0xFF, 0xBB }, rec.Data);
    }

    [Fact]
    public void DoBinary_Parses_AsNegotiate()
    {
        var p = new TelnetParser();
        var msgs = p.Feed(new byte[] { IAC, DO, BIN });

        var n = Assert.IsType<TelnetNegotiate>(Assert.Single(msgs));
        Assert.Equal(DO, n.Command);
        Assert.Equal(BIN, n.Option);
    }

    [Fact]
    public void TerminalTypeSubneg_SendCommand_Parses()
    {
        var p = new TelnetParser();
        // IAC SB TERMINAL-TYPE SEND IAC SE
        var msgs = p.Feed(new byte[] { IAC, SB, TT, 0x01, IAC, SE });

        var s = Assert.IsType<TelnetSubneg>(Assert.Single(msgs));
        Assert.Equal(TT, s.Option);
        Assert.Equal(new byte[] { 0x01 }, s.Data);
    }

    [Fact]
    public void Subneg_WithIacIacInData_IsUnescaped()
    {
        var p = new TelnetParser();
        var msgs = p.Feed(new byte[] { IAC, SB, TT, 0x00, 0xAA, IAC, IAC, 0xBB, IAC, SE });

        var s = Assert.IsType<TelnetSubneg>(Assert.Single(msgs));
        Assert.Equal(TT, s.Option);
        Assert.Equal(new byte[] { 0x00, 0xAA, 0xFF, 0xBB }, s.Data);
    }

    [Fact]
    public void State_Persists_AcrossSplitFeeds()
    {
        var p = new TelnetParser();
        Assert.Empty(p.Feed(new byte[] { 0x01, 0x02 }));
        Assert.Empty(p.Feed(new byte[] { IAC }));
        var msgs = p.Feed(new byte[] { EOR });

        var rec = Assert.IsType<TelnetRecord>(Assert.Single(msgs));
        Assert.Equal(new byte[] { 0x01, 0x02 }, rec.Data);
    }

    [Fact]
    public void Negotiation_SplitAcrossFeeds_Parses()
    {
        var p = new TelnetParser();
        Assert.Empty(p.Feed(new byte[] { IAC, DO }));
        var msgs = p.Feed(new byte[] { BIN });

        var n = Assert.IsType<TelnetNegotiate>(Assert.Single(msgs));
        Assert.Equal(DO, n.Command);
        Assert.Equal(BIN, n.Option);
    }

    [Fact]
    public void MultipleMessages_InOneFeed_AllEmitted()
    {
        var p = new TelnetParser();
        // DO BIN, WILL EOR, then a record "Hi" + IAC EOR
        var msgs = p.Feed(new byte[]
        {
            IAC, DO, BIN,
            IAC, WILL, TelnetCodes.OptEndOfRecord,
            0x48, 0x69, IAC, EOR,
        });

        Assert.Equal(3, msgs.Count);
        Assert.IsType<TelnetNegotiate>(msgs[0]);
        Assert.IsType<TelnetNegotiate>(msgs[1]);
        var rec = Assert.IsType<TelnetRecord>(msgs[2]);
        Assert.Equal(new byte[] { 0x48, 0x69 }, rec.Data);
    }

    [Fact]
    public void UnknownIacCommand_IsIgnored()
    {
        var p = new TelnetParser();
        // IAC 0xF1 (NOP) inside a stream should be swallowed
        var msgs = p.Feed(new byte[] { 0xAA, IAC, 0xF1, 0xBB, IAC, EOR });

        var rec = Assert.IsType<TelnetRecord>(Assert.Single(msgs));
        Assert.Equal(new byte[] { 0xAA, 0xBB }, rec.Data);
    }
}
