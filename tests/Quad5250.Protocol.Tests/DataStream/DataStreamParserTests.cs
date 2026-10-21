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
    public void Wtd_RepeatToAddress_NullFill_WritesSpaces()
    {
        var (p, s) = NewParser();
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x01, 0x01,
            Order.RepeatToAddress, 0x01, 0x05, 0x00));

        for (int c = 0; c < 5; c++)
            Assert.Equal(' ', s[0, c].Char);
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
    public void Wtd_WithUnlockBit_ReleasesKeyboard()
    {
        var (p, _) = NewParser();

        var result = p.Apply(Record(0x04, Cmd.WriteToDisplay, 0x00, 0x18, 0xC1));

        Assert.True(result.KeyboardUnlocked);
        Assert.True(result.ReleasesKeyboard);
    }

    [Fact]
    public void Wtd_WithoutUnlockBit_KeepsKeyboardLocked()
    {
        // Main Menu's first WTD (CC2 = 0x00) draws the screen but doesn't release it.
        var (p, _) = NewParser();

        var result = p.Apply(Record(0x04, Cmd.ClearUnit, 0x04, Cmd.WriteToDisplay, 0x00, 0x00, 0xC1));

        Assert.False(result.ReleasesKeyboard);
    }

    [Fact]
    public void ReadMdtCommand_AfterClassPrefix_RequestsRead()
    {
        var (p, _) = NewParser();

        var result = p.Apply(Record(0x04, Cmd.WriteToDisplay, 0x00, 0x00, 0xC1, 0x04, Cmd.ReadMdtFields, 0x00, 0x00));

        Assert.True(result.ReadRequested);
        Assert.False(result.KeyboardUnlocked);
        Assert.True(result.ReleasesKeyboard);
    }

    [Fact]
    public void ReadCommandByte_WithoutClassPrefix_IsIgnored()
    {
        var (p, _) = NewParser();

        var result = p.Apply(Record(0x00, Cmd.ReadMdtFields, 0x00, 0x00));

        Assert.False(result.ReadRequested);
    }

    [Fact]
    public void SaveScreen_RepliesWithStreamThatRestoreReplays()
    {
        var (p, s) = NewParser();
        // A screen with a label, an input field holding host text, an
        // output-only field and the cursor on the input field.
        p.Apply(Record(
            0x04, Cmd.ClearUnit,
            0x04, Cmd.WriteToDisplay, 0x00, 0x18,
            Order.SetBufferAddress, 0x02, 0x02, 0x22, 0xC1, 0xC2,          // "AB" highlighted
            Order.SetBufferAddress, 0x05, 0x0A,
            Order.StartOfField, 0x40, 0x20, 0x24, 0x00, 0x04, 0xC3,        // input "C", monocase
            Order.SetBufferAddress, 0x07, 0x0A,
            Order.StartOfField, 0x22, 0x00, 0x03, 0xC4,                    // output-only "D"
            Order.InsertCursor, 0x05, 0x0B));
        new Quad5250.Protocol.Input.FieldEditor(s).FillField(s.Fields[0], "XY");
        var before = s.TakeSnapshot();

        // Save Screen: the reply is a Put/Get record carrying 04 12 + the stream.
        p.Apply(Record(0x04, Cmd.SaveScreen));
        Assert.True(p.TryDequeueResponse(out var reply));
        Assert.Equal(reply.Length, (reply[0] << 8) | reply[1]);
        Assert.Equal(new byte[] { 0x12, 0xA0, 0x00, 0x00, 0x04, 0x00, 0x00, 0x03, 0x04, Cmd.RestoreScreen }, reply[2..12]);

        // A window draws over the screen, then the host restores it by
        // sending our bytes back.
        p.Apply(Record(0x04, Cmd.ClearUnit, 0x04, Cmd.WriteToDisplay, 0x00, 0x00, 0xE6));
        p.Apply(Record(reply[10..]));
        var after = s.TakeSnapshot();

        Assert.Equal(before.Cells, after.Cells);
        // Restored fields match, except that the operator's change now rides
        // on the FFW MDT bit (0x08), which is what keeps it "modified".
        Assert.Equal(before.Fields, after.Fields.Select(f => f with { Ffw1 = (byte)(f.Ffw1 & ~0x08) }));
        Assert.Equal((before.CursorRow, before.CursorCol), (after.CursorRow, after.CursorCol));
        Assert.True(after.IsModified(after.Fields[0]));
        Assert.False(after.Fields[1].IsInput);
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
    public void StartOfField_WithFfw_DefinesInputField()
    {
        var (p, s) = NewParser();
        // SBA(6,20), SF FFW=0x60 0x20 (bypass, monocase), attr 0x24, len 10
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x06, 0x14,
            Order.StartOfField, 0x60, 0x20, 0x24, 0x00, 0x0A));

        var f = Assert.Single(s.Fields);
        Assert.Equal((5, 20, 10), (f.Row, f.Col, f.Length));
        Assert.True(f.IsInput);
        Assert.True(f.IsBypass);
        Assert.True(f.IsMonocase);
        Assert.False(f.IsNumeric);
    }

    [Fact]
    public void AttributeRewrittenAfterField_RestylesFieldCells()
    {
        // Main Menu command line as IBM i sends it: SF with attr 0x20 first,
        // other screen content, then the same position rewritten inline as
        // 0x24 (underscore) after the "===>" prompt, then a 0x20 to end it.
        var (p, s) = NewParser();
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x14, 0x06,
            Order.StartOfField, 0x50, 0x00, 0x20, 0x00, 0x05,
            Order.SetBufferAddress, 0x01, 0x01, 0xC1,
            Order.SetBufferAddress, 0x14, 0x01, 0x20, 0x7E, 0x7E, 0x7E, 0x6E, 0x24,
            Order.SetBufferAddress, 0x14, 0x0C, 0x20));

        for (int c = 6; c <= 10; c++)
            Assert.Equal((byte)0x24, s[19, c].Attribute);
        Assert.Equal((byte)0x20, s[19, 12].Attribute);
        Assert.Equal('=', s[19, 1].Char);
        var f = Assert.Single(s.Fields);
        Assert.Equal((byte)0x24, f.Attribute);
        Assert.Equal(5, f.Length);
    }

    [Fact]
    public void Cells_TakeAttributeOfNearestPrecedingAttributeByte()
    {
        var (p, s) = NewParser();
        // Text first, then an attribute byte placed before it in a later record.
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x02, 0x05, 0xC1, 0xC2));
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x02, 0x04, 0x28));

        Assert.Equal((byte)0x28, s[1, 4].Attribute);
        Assert.Equal((byte)0x28, s[1, 5].Attribute);
        Assert.Equal((byte)0x20, s[0, 0].Attribute);
    }

    [Fact]
    public void SfFieldAttribute_StopsAtFieldLength()
    {
        // Sign On style: "AB" under 0x20, SF attr 0x24 len 3, then NULL padding
        // with no ending attribute byte, then more text.
        var (p, s) = NewParser();
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x06, 0x01, 0x20, 0xC1, 0xC2,
            Order.StartOfField, 0x40, 0x00, 0x24, 0x00, 0x03,
            0x00, 0x00, 0x00, 0x00, 0x00, 0xC3));

        Assert.Equal((byte)0x24, s[5, 4].Attribute);
        Assert.Equal((byte)0x24, s[5, 6].Attribute);
        Assert.Equal((byte)0x20, s[5, 7].Attribute);
        Assert.Equal((byte)0x20, s[5, 9].Attribute);
        Assert.Equal('C', s[5, 9].Char);
        Assert.Equal((byte)0x20, s[10, 0].Attribute);
    }

    [Fact]
    public void StartOfField_WithoutFfw_DefinesOutputOnlyField()
    {
        var (p, s) = NewParser();
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x06, 0x14,
            Order.StartOfField, 0x22, 0x00, 0x05,
            0xC1));

        var f = Assert.Single(s.Fields);
        Assert.False(f.IsInput);
        Assert.Equal((byte)0x22, f.Attribute);
        Assert.Equal(5, f.Length);
        Assert.Equal('A', s[5, 20].Char);
    }

    [Fact]
    public void StartOfField_SkipsFieldControlWords()
    {
        var (p, s) = NewParser();
        // FFW 0x45 0x00 (digits only), FCW 0x88 0x00, attr 0x24, len 3
        p.Apply(Record(
            Cmd.WriteToDisplay, 0x00, 0x00,
            Order.SetBufferAddress, 0x06, 0x14,
            Order.StartOfField, 0x45, 0x00, 0x88, 0x00, 0x24, 0x00, 0x03));

        var f = Assert.Single(s.Fields);
        Assert.Equal((byte)0x24, f.Attribute);
        Assert.Equal(3, f.Length);
        Assert.Equal(FieldShift.DigitsOnly, f.Shift);
        Assert.True(f.IsNumeric);
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
        Assert.True(s[0, 0].IsAttributeByte);
        Assert.Equal('A', s[0, 1].Char);
        Assert.False(s[0, 1].IsAttributeByte);
    }
}
