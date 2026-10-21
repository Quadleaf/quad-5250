using System.Text;

namespace Quad5250.Protocol.Screen;

/// <summary>
/// Point-in-time copy of a <see cref="ScreenBuffer"/> for renderers and other
/// readers that run off the session's read thread. Cells is row-major,
/// length = Rows * Cols.
/// </summary>
public sealed record ScreenSnapshot(
    int Rows,
    int Cols,
    int CursorRow,
    int CursorCol,
    ScreenCell[] Cells,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlySet<int> ModifiedFieldStarts,
    bool KeyboardLocked)
{
    public static ScreenSnapshot Blank(int rows = 24, int cols = 80)
    {
        var cells = new ScreenCell[rows * cols];
        Array.Fill(cells, ScreenCell.Empty);
        return new(rows, cols, 0, 0, cells, Array.Empty<FieldDefinition>(), new HashSet<int>(), false);
    }

    public ScreenCell this[int row, int col] => Cells[row * Cols + col];

    public bool IsModified(FieldDefinition field) => ModifiedFieldStarts.Contains(field.StartIndex(Cols));

    /// <summary>The field covering a cell, if any.</summary>
    public FieldDefinition? FieldAt(int row, int col)
    {
        var index = row * Cols + col;
        foreach (var f in Fields)
            if (f.Contains(index, Cols)) return f;
        return null;
    }

    /// <summary>
    /// True when the cell's content must not be shown: a non-display field
    /// (passwords) or a cell whose attribute hides it.
    /// </summary>
    public bool IsHidden(int row, int col)
    {
        if ((this[row, col].Attribute & 0x07) == 0x07) return true;
        return FieldAt(row, col)?.IsNonDisplay == true;
    }

    /// <summary>One row as text, with hidden cells (passwords) replaced by spaces.</summary>
    public string RowText(int row)
    {
        var sb = new StringBuilder(Cols);
        for (int c = 0; c < Cols; c++)
            sb.Append(IsHidden(row, c) ? ' ' : this[row, c].Char);
        return sb.ToString();
    }

    /// <summary>The whole screen as text, one line per row, hidden cells blanked.</summary>
    public string ToText() => string.Join('\n', Enumerable.Range(0, Rows).Select(RowText));
}
