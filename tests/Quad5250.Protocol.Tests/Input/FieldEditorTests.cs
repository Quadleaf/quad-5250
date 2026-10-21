using Quad5250.Protocol.Input;
using Quad5250.Protocol.Screen;
using Xunit;

namespace Quad5250.Protocol.Tests.Input;

public class FieldEditorTests
{
    private const byte Input = 0x40;          // FFW1: FFW present, alpha shift
    private const byte Bypass = 0x60;         // FFW1: FFW present + bypass
    private const byte DigitsOnly = 0x45;     // FFW1: FFW present, shift 5
    private const byte Monocase = 0x20;       // FFW2 bit 2
    private const byte AutoEnter = 0x80;      // FFW2 bit 0

    /// <summary>Place an SF-defined field whose first editable cell is (row, col).</summary>
    private static void AddField(ScreenBuffer s, int row, int col, int length,
        byte ffw1 = Input, byte ffw2 = 0, byte attr = 0x24)
    {
        var attrIndex = row * s.Cols + col - 1;
        s.SetBufferAddress(attrIndex / s.Cols, attrIndex % s.Cols);
        s.WriteAttribute(attr, ffw1, ffw2, fromSf: true, length: length);
    }

    private static (ScreenBuffer Screen, FieldEditor Editor) Setup(Action<ScreenBuffer> build)
    {
        var s = new ScreenBuffer();
        build(s);
        s.RebuildFields();
        return (s, new FieldEditor(s));
    }

    [Fact]
    public void OnHostWrite_CursorAtOrigin_MovesToFirstEnterableField()
    {
        var (s, e) = Setup(s =>
        {
            AddField(s, 3, 10, 5, ffw1: Bypass);
            AddField(s, 5, 20, 10);
        });

        e.OnHostWrite();

        Assert.Equal((5, 20), (s.CursorRow, s.CursorCol));
    }

    [Fact]
    public void OnHostWrite_HostPlacedCursor_IsKept()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 10));
        s.SetCursor(7, 7);

        e.OnHostWrite();

        Assert.Equal((7, 7), (s.CursorRow, s.CursorCol));
    }

    [Fact]
    public void TypeChar_WritesIntoBuffer_AndAdvances()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 10));
        e.Home();

        Assert.Equal(EditResult.Ok, e.TypeChar('a'));
        Assert.Equal(EditResult.Ok, e.TypeChar('b'));

        Assert.Equal('a', s[5, 20].Char);
        Assert.Equal('b', s[5, 21].Char);
        Assert.Equal((5, 22), (s.CursorRow, s.CursorCol));
        Assert.True(s.IsModified(s.Fields[0]));
    }

    [Fact]
    public void TypeChar_KeepsFieldAttribute()
    {
        var (s, e) = Setup(s =>
        {
            AddField(s, 5, 20, 10, attr: 0x27);
            // Host pads the field after the SF; the cells take the sticky attribute.
            for (int i = 0; i < 10; i++) s.Write(' ');
        });
        e.Home();

        e.TypeChar('x');

        Assert.Equal((byte)0x27, s[5, 20].Attribute);
    }

    [Fact]
    public void TypeChar_OutsideField_IsRejected()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 10));
        e.SetCursor(0, 0);

        Assert.Equal(EditResult.ProtectedPosition, e.TypeChar('a'));
        Assert.Equal(' ', s[0, 0].Char);
    }

    [Fact]
    public void TypeChar_IntoBypassField_IsRejected()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 10, ffw1: Bypass));
        e.SetCursor(5, 20);

        Assert.Equal(EditResult.ProtectedPosition, e.TypeChar('a'));
    }

    [Fact]
    public void TypeChar_IntoOutputOnlyField_IsRejected()
    {
        // SF without an FFW: Ffw1 = 0.
        var (s, e) = Setup(s => AddField(s, 5, 20, 10, ffw1: 0));
        e.SetCursor(5, 20);

        Assert.Equal(EditResult.ProtectedPosition, e.TypeChar('a'));
    }

    [Fact]
    public void TypeChar_DigitsOnly_RejectsLetters()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 4, ffw1: DigitsOnly));
        e.Home();

        Assert.Equal(EditResult.InvalidCharacter, e.TypeChar('a'));
        Assert.Equal(EditResult.Ok, e.TypeChar('7'));
        Assert.Equal('7', s[5, 20].Char);
    }

    [Fact]
    public void TypeChar_Monocase_Uppercases()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 4, ffw2: Monocase));
        e.Home();

        e.TypeChar('q');

        Assert.Equal('Q', s[5, 20].Char);
    }

    [Fact]
    public void TypeChar_LastPosition_JumpsToNextField()
    {
        var (s, e) = Setup(s =>
        {
            AddField(s, 5, 20, 2);
            AddField(s, 6, 20, 2);
        });
        e.Home();

        e.TypeChar('a');
        e.TypeChar('b');

        Assert.Equal((6, 20), (s.CursorRow, s.CursorCol));
    }

    [Fact]
    public void TypeChar_LastPositionOfAutoEnterField_ReportsAutoEnter()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 1, ffw2: AutoEnter));
        e.Home();

        Assert.Equal(EditResult.AutoEnter, e.TypeChar('Y'));
    }

    [Fact]
    public void Field_WrappingAcrossRows_IsEditedLinearly()
    {
        var (s, e) = Setup(s => AddField(s, 5, 78, 4));
        e.Home();

        foreach (var ch in "abcd") e.TypeChar(ch);

        Assert.Equal('a', s[5, 78].Char);
        Assert.Equal('b', s[5, 79].Char);
        Assert.Equal('c', s[6, 0].Char);
        Assert.Equal('d', s[6, 1].Char);
    }

    [Fact]
    public void Backspace_BlanksPreviousCell_AndStopsAtFieldStart()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 10));
        e.Home();
        e.TypeChar('a');
        e.TypeChar('b');

        e.Backspace();
        e.Backspace();
        e.Backspace();

        Assert.Equal(' ', s[5, 20].Char);
        Assert.Equal(' ', s[5, 21].Char);
        Assert.Equal((5, 20), (s.CursorRow, s.CursorCol));
    }

    [Fact]
    public void Delete_ShiftsRestOfFieldLeft()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 4));
        e.FillField(s.Fields[0], "abcd");
        e.SetCursor(5, 21);

        e.Delete();

        Assert.Equal("acd", e.ReadField(s.Fields[0]));
    }

    [Fact]
    public void EraseToEndOfField_BlanksFromCursor()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 4));
        e.FillField(s.Fields[0], "abcd");
        e.SetCursor(5, 22);

        e.EraseToEndOfField();

        Assert.Equal("ab", e.ReadField(s.Fields[0]));
    }

    [Fact]
    public void Tab_CyclesEnterableFields_SkippingBypass()
    {
        var (s, e) = Setup(s =>
        {
            AddField(s, 2, 10, 5);
            AddField(s, 3, 10, 5, ffw1: Bypass);
            AddField(s, 4, 10, 5);
        });
        e.Home();

        e.TabForward();
        Assert.Equal((4, 10), (s.CursorRow, s.CursorCol));
        e.TabForward();
        Assert.Equal((2, 10), (s.CursorRow, s.CursorCol));
        e.TabBackward();
        Assert.Equal((4, 10), (s.CursorRow, s.CursorCol));
    }

    [Fact]
    public void TabBackward_FromInsideField_GoesToItsStartFirst()
    {
        var (s, e) = Setup(s =>
        {
            AddField(s, 2, 10, 5);
            AddField(s, 4, 10, 5);
        });
        e.SetCursor(4, 12);

        e.TabBackward();

        Assert.Equal((4, 10), (s.CursorRow, s.CursorCol));
    }

    [Fact]
    public void ModifiedFields_KeepHostContent_WhenOperatorEditsPart()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 6));
        // Host prefilled "ABC" in the field.
        s.SetBufferAddress(5, 20);
        foreach (var ch in "ABC") s.Write(ch, 0x24);
        e.SetCursor(5, 23);

        e.TypeChar('D');

        var mod = Assert.Single(e.GetModifiedFields());
        Assert.Equal((5, 20, "ABCD"), (mod.Row, mod.Col, mod.Text));
    }

    [Fact]
    public void ModifiedFields_ExcludeUntouchedFields_IncludeHostMdt()
    {
        var (s, e) = Setup(s =>
        {
            AddField(s, 2, 10, 5);
            AddField(s, 3, 10, 5, ffw1: Input | 0x08); // MDT set by host
        });

        var mod = Assert.Single(e.GetModifiedFields());
        Assert.Equal(3, mod.Row);
    }

    [Fact]
    public void TypedInput_SurvivesPartialHostWrite()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 6));
        e.Home();
        e.TypeChar('x');

        // Host writes an error message on row 24 without clearing the screen.
        s.SetBufferAddress(23, 0);
        s.Write('E', 0x28);
        s.RebuildFields();

        Assert.Equal('x', s[5, 20].Char);
        Assert.Equal("x", Assert.Single(e.GetModifiedFields()).Text);
    }

    [Fact]
    public void ClearUnit_ResetsOperatorModified()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 6));
        e.Home();
        e.TypeChar('x');

        s.Clear();
        AddField(s, 5, 20, 6);
        s.RebuildFields();

        Assert.Empty(e.GetModifiedFields());
    }

    [Fact]
    public void FillField_RejectsTooLongText()
    {
        var (s, e) = Setup(s => AddField(s, 5, 20, 3));

        Assert.Equal(EditResult.InvalidCharacter, e.FillField(s.Fields[0], "abcd"));
        Assert.Equal("", e.ReadField(s.Fields[0]));
    }

    [Fact]
    public void Field_PastEndOfScreen_IsClamped()
    {
        var (s, e) = Setup(s => AddField(s, 23, 75, 50));

        Assert.Equal(5, s.Fields[0].Length);
        e.Home();
        for (int i = 0; i < 10; i++) e.TypeChar('z');
        Assert.Equal("zzzzz", e.ReadField(s.Fields[0]));
    }
}
