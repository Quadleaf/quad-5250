namespace Quad5250.Protocol.Screen;

/// <summary>
/// 5250 screen buffer: a fixed grid of <see cref="ScreenCell"/> with two
/// independent positions:
/// <list type="bullet">
///   <item>Buffer position — where the parser writes (set by SBA, advanced by writes).</item>
///   <item>Cursor position — where the user's typing cursor displays (set by IC).</item>
/// </list>
/// The wire format pushes the cursor position to the browser.
/// </summary>
public sealed class ScreenBuffer
{
    public int Rows { get; }
    public int Cols { get; }

    public int CursorRow { get; private set; }
    public int CursorCol { get; private set; }

    public int BufferRow { get; private set; }
    public int BufferCol { get; private set; }

    /// <summary>
    /// Most-recent attribute byte the host sent. 5250 attribute bytes are
    /// "sticky" — every character after one inherits its color/intensity until
    /// the next attribute byte. Reset to 0x20 (green-on-black) on Clear.
    /// </summary>
    public byte CurrentAttribute { get; private set; } = 0x20;

    private readonly ScreenCell[] _cells;
    private readonly List<FieldDefinition> _fields = new();
    // Position (row*Cols+col) → attribute mark for every attribute byte the
    // host has emitted on the current screen. Re-writing the same position
    // (e.g. partial screen update) overwrites the prior mark.
    private readonly Dictionary<int, AttrMark> _attrMarks = new();

    public IReadOnlyList<FieldDefinition> Fields => _fields;

    public ScreenBuffer(int rows = 24, int cols = 80)
    {
        Rows = rows;
        Cols = cols;
        _cells = new ScreenCell[rows * cols];
        Clear();
    }

    private readonly record struct AttrMark(int Row, int Col, byte Attr, byte Ffw1, byte Ffw2, bool FromSf, int Length);

    public ScreenCell this[int row, int col]
    {
        get => _cells[row * Cols + col];
        set => _cells[row * Cols + col] = value;
    }

    public void Clear()
    {
        Array.Fill(_cells, ScreenCell.Empty);
        CursorRow = 0;
        CursorCol = 0;
        BufferRow = 0;
        BufferCol = 0;
        CurrentAttribute = 0x20;
        _fields.Clear();
        _attrMarks.Clear();
    }

    /// <summary>
    /// Write an attribute byte at the current buffer position. The cell
    /// displays as space and the position is recorded for later
    /// <see cref="RebuildFields"/> scans.
    /// </summary>
    public void WriteAttribute(byte attr, byte ffw1 = 0, byte ffw2 = 0, bool fromSf = false, int length = 0)
    {
        var key = BufferRow * Cols + BufferCol;
        // The host sometimes re-emits an inline attribute byte at a position
        // that already holds an SF mark (e.g. Sign On's Password field, where
        // an inline 0x27 + RA repaints the input area after the SF defines
        // it). The visual cell update still happens, but we must not let the
        // inline mark overwrite the SF's field semantics — length and FFW
        // would be lost and the field would disappear from the field list.
        var keepExistingSf = !fromSf
            && _attrMarks.TryGetValue(key, out var existing)
            && existing.FromSf;
        if (!keepExistingSf)
            _attrMarks[key] = new AttrMark(BufferRow, BufferCol, attr, ffw1, ffw2, fromSf, length);
        Write(' ', attr);
        CurrentAttribute = attr;
    }

    /// <summary>Write a character at the current buffer position using the
    /// current sticky attribute.</summary>
    public void Write(char ch)
    {
        _cells[BufferRow * Cols + BufferCol] = new ScreenCell(ch, CurrentAttribute);
        AdvanceBuffer();
    }

    /// <summary>
    /// Rebuild the field list from the recorded attribute byte positions.
    /// 5250 fields are delimited by attribute bytes — an input attribute
    /// (bit 2 set) starts a field at the next cell, ending at the next
    /// attribute byte (or end of screen).
    /// </summary>
    public void RebuildFields()
    {
        _fields.Clear();
        if (_attrMarks.Count == 0) return;

        var sorted = _attrMarks
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .ToList();

        for (int k = 0; k < sorted.Count; k++)
        {
            var m = sorted[k];
            // In 5250 enhanced mode (which we declared in Query Reply), input
            // fields are defined exclusively by Start-of-Field orders.
            // Inline attribute bytes with bit 2 set look like input markers
            // by attribute-byte convention but in practice carry visual-only
            // hints (e.g. system status markers around the date row of
            // WRKUSRJOB) — treating them as input fields produces stray
            // underscores the IBM emulator doesn't show.
            if (!m.FromSf || m.Length <= 0) continue;

            var startIdx = m.Row * Cols + m.Col + 1;
            // SF orders carry an explicit length. Use it directly — relying
            // on the scan-distance to the next attribute mark fails when the
            // host pads with NULLs between fields (e.g. User -> Password on
            // the Sign On screen has 10 NULLs separating the SFs, not an
            // intervening attribute byte).
            var length = m.Length;

            _fields.Add(new FieldDefinition(
                startIdx / Cols, startIdx % Cols, length, m.Attr, m.Ffw1, m.Ffw2));
        }
    }

    public void SetCursor(int row, int col)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorCol = Math.Clamp(col, 0, Cols - 1);
    }

    public void SetBufferAddress(int row, int col)
    {
        BufferRow = Math.Clamp(row, 0, Rows - 1);
        BufferCol = Math.Clamp(col, 0, Cols - 1);
    }

    /// <summary>Write at the current buffer position and advance.</summary>
    public void Write(char ch, byte attr)
    {
        _cells[BufferRow * Cols + BufferCol] = new ScreenCell(ch, attr);
        AdvanceBuffer();
    }

    public void AdvanceBuffer()
    {
        BufferCol++;
        if (BufferCol >= Cols)
        {
            BufferCol = 0;
            BufferRow = (BufferRow + 1) % Rows;
        }
    }

    public ScreenCell[] Snapshot()
    {
        var copy = new ScreenCell[_cells.Length];
        Array.Copy(_cells, copy, _cells.Length);
        return copy;
    }
}
