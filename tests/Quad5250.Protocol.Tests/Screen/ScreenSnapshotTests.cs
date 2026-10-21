using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Input;
using Quad5250.Protocol.Screen;
using Xunit;

namespace Quad5250.Protocol.Tests.Screen;

public class ScreenSnapshotTests
{
    private static void WriteText(ScreenBuffer s, int row, int col, string text)
    {
        s.SetBufferAddress(row, col);
        foreach (var ch in text) s.Write(ch, 0x20);
    }

    [Fact]
    public void RowText_MasksNonDisplayFields()
    {
        var s = new ScreenBuffer();
        s.SetBufferAddress(5, 19);
        s.WriteAttribute(0x27, 0x40, 0x00, fromSf: true, length: 6);
        s.RebuildFields();
        var editor = new FieldEditor(s);
        editor.FillField(s.Fields[0], "secret");

        var snap = s.TakeSnapshot();

        Assert.DoesNotContain("secret", snap.ToText());
        Assert.True(snap.IsModified(snap.Fields[0]));
    }

    [Fact]
    public void FunctionKeyLabels_FindsLegendsInBottomRows()
    {
        var s = new ScreenBuffer();
        WriteText(s, 22, 1, "F3=Exit   F13=Information Assistant  F24=More keys");
        WriteText(s, 2, 1, "F5=Ignored because it is not near the bottom");

        var labels = FunctionKeyLabels.Find(s.TakeSnapshot());

        Assert.Collection(labels,
            l => Assert.Equal((Aid.F3, 22, 1, 8, "F3=Exit"), (l.Aid, l.Row, l.StartCol, l.EndCol, l.Text)),
            l => Assert.Equal((Aid.F13, "F13=Information Assistant"), (l.Aid, l.Text)),
            l => Assert.Equal((Aid.F24, "F24=More keys"), (l.Aid, l.Text)));
    }
}
