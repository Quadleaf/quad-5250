using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Ebcdic;
using Quad5250.Protocol.Screen;
using Xunit;

namespace Quad5250.Protocol.Tests.DataStream;

public class DataStreamParserTests
{
    private static (DataStreamParser Parser, ScreenBuffer Screen) NewParser()
    {
        var screen = new ScreenBuffer();
        var ebc = new EbcdicTranslator();
        return (new DataStreamParser(screen, ebc), screen);
    }

    /// <summary>Prepend an 8-byte GDS+opcode header to the given command bytes.</summary>
    private static byte[] Record(params byte[] body)
    {
        var record = new byte[8 + body.Length];
        record[0] = 0x00;
        record[1] = (byte)(8 + body.Length);
        record[2] = 0x12;
        record[3] = 0xA0;
        record[4] = 0x00;
        record[5] = 0x00;
        record[6] = 0x00;
        record[7] = 0x00;
        Array.Copy(body, 0, record, 8, body.Length);
        return record;
    }

    [Fact]
    public void ClearUnit_ClearsScreen()
    {
        var (p, s) = NewParser();
        s[0, 0] = new ScreenCell('X', 0x20);

        p.Apply(Record(Cmd.ClearUnit));

        Assert.Equal(' ', s[0, 0].Char);
        Assert.Equal(0, s.BufferRow);
        Assert.Equal(0, s.BufferCol);
    }

    [Fact]
    public void Wtd_Sba_Text_RendersAtAddress()
    {
        var (p, s) = NewParser();
        // Write to Display, CC1=0, CC2=0, SBA(2,5), 'A','B','C'
        // EBCDIC: A=0xC1, B=0xC2, C=0xC3
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x02, 0x05,
            0xC1, 0xC2, 0xC3));

        Assert.Equal('A', s[1, 4].Char);
        Assert.Equal('B', s[1, 5].Char);
        Assert.Equal('C', s[1, 6].Char);
        Assert.Equal(' ', s[1, 7].Char);
    }

    [Fact]
    public void Wtd_InsertCursor_SetsCursor_NotBuffer()
    {
        var (p, s) = NewParser();
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x05, 0x0A,
            0xC1, // 'A' at row 4, col 9; buffer advances to (4,10)
            Order.InsertCursor, 0x07, 0x14));

        // Buffer position is one past the written char.
        Assert.Equal(4, s.BufferRow);
        Assert.Equal(10, s.BufferCol);
        // Cursor moved by IC (1-indexed → 0-indexed).
        Assert.Equal(6, s.CursorRow);
        Assert.Equal(19, s.CursorCol);
    }

    [Fact]
    public void Wtd_RepeatToAddress_FillsRange()
    {
        var (p, s) = NewParser();
        // SBA(1,1), RA to (1,10) with '-' (EBCDIC 0x60)
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x01, 0x01,
            Order.RepeatToAddress, 0x01, 0x0A, 0x60));

        for (int c = 0; c < 10; c++)
            Assert.Equal('-', s[0, c].Char);
        Assert.Equal(' ', s[0, 10].Char);
    }

    [Fact]
    public void MultipleCommands_InOneRecord_Apply_InOrder()
    {
        var (p, s) = NewParser();
        s[0, 0] = new ScreenCell('X', 0x20);

        p.Apply(Record(
            Cmd.ClearUnit,
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x01, 0x01,
            0xC8, 0x89)); // 'H', 'i' — wait: 'i' is 0x89, 'H' is 0xC8

        Assert.Equal('H', s[0, 0].Char);
        Assert.Equal('i', s[0, 1].Char);
    }

    [Fact]
    public void HeaderOnly_Record_IsNoOp()
    {
        var (p, s) = NewParser();
        s[0, 0] = new ScreenCell('X', 0x20);

        p.Apply(new byte[] { 0x00, 0x08, 0x12, 0xA0, 0x00, 0x00, 0x00, 0x00 });

        Assert.Equal('X', s[0, 0].Char);
    }

    [Fact]
    public void AttributeByte_OccupiesOneCell_AsSpace()
    {
        var (p, s) = NewParser();
        // SBA(1,1), attribute 0x20, 'A'
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x01, 0x01,
            0x20, // attribute byte — takes one cell
            0xC1)); // 'A'

        Assert.Equal(' ', s[0, 0].Char);
        Assert.Equal((byte)0x20, s[0, 0].Attribute);
        Assert.Equal('A', s[0, 1].Char);
    }
}
