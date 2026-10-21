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
    // Start addresses of fields the operator has changed (the MDT bit the
    // terminal sets locally). Keyed by address because RebuildFields replaces
    // the FieldDefinition instances after every host record.
    private readonly HashSet<int> _operatorModified = new();

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
        _operatorModified.Clear();
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
        // The new attribute value does take effect, though: the Main Menu
        // defines its command line with SF attr 0x20, then rewrites that
        // position inline as 0x24 to underscore it.
        _attrMarks[key] = !fromSf && _attrMarks.TryGetValue(key, out var existing) && existing.FromSf
            ? existing with { Attr = attr }
            : new AttrMark(BufferRow, BufferCol, attr, ffw1, ffw2, fromSf, length);
        _cells[BufferRow * Cols + BufferCol] = new ScreenCell(' ', attr, IsAttributeByte: true);
        AdvanceBuffer();
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
    /// Give every cell the attribute of the nearest attribute byte before it.
    /// On a 5250 an attribute byte governs all following positions up to the
    /// next one, whatever order the host wrote them in — e.g. an attribute
    /// rewritten after the field's cells were filled still restyles them.
    /// An SF-defined field's attribute covers only the field's declared
    /// length; after it the attribute in effect before the field resumes.
    /// Sign On relies on this: its fields are padded with NULLs, not closed
    /// by an ending attribute byte. Cells before the first attribute byte
    /// use the default 0x20.
    /// </summary>
    public void ResolveAttributes()
    {
        byte current = 0x20;
        byte resumeAttr = 0x20;
        int resumeAt = -1;
        for (int i = 0; i < _cells.Length; i++)
        {
            var cell = _cells[i];
            if (cell.IsAttributeByte)
            {
                resumeAt = -1;
                if (_attrMarks.TryGetValue(i, out var mark) && mark.FromSf && mark.Length > 0)
                {
                    resumeAttr = current;
                    resumeAt = i + 1 + mark.Length;
                }
                current = cell.Attribute;
                continue;
            }
            if (i == resumeAt)
            {
                current = resumeAttr;
                resumeAt = -1;
            }
            if (cell.Attribute != current) _cells[i] = cell with { Attribute = current };
        }
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
            // Clamp to the end of the buffer so a malformed or edge-of-screen
            // SF can't produce a field that addresses past the last cell.
            var total = Rows * Cols;
            if (startIdx >= total) continue;
            var length = Math.Min(m.Length, total - startIdx);

            _fields.Add(new FieldDefinition(
                startIdx / Cols, startIdx % Cols, length, m.Attr, m.Ffw1, m.Ffw2));
        }

        // Drop modified marks for fields the host has since removed.
        _operatorModified.RemoveWhere(start => !_fields.Any(f => f.StartIndex(Cols) == start));
    }

    /// <summary>Set the operator-side MDT for a field (it has been typed into).</summary>
    public void MarkModified(FieldDefinition field) => _operatorModified.Add(field.StartIndex(Cols));

    /// <summary>True if the operator changed the field or the host sent it with MDT set.</summary>
    public bool IsModified(FieldDefinition field)
        => field.IsHostModified || _operatorModified.Contains(field.StartIndex(Cols));

    /// <summary>Cell at a linear buffer address (row * Cols + col).</summary>
    public ScreenCell CellAt(int index) => _cells[index];

    /// <summary>Replace the character at a linear address, keeping its attribute.</summary>
    public void SetCharAt(int index, char ch) => _cells[index] = _cells[index] with { Char = ch };

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

    /// <summary>
    /// The screen as a 5250 data stream that redraws it exactly: Clear Unit,
    /// then one Write to Display re-emitting every attribute byte, SF order
    /// (fields the operator changed keep their MDT bit) and character, then
    /// the cursor. Used to answer Save Screen: the host keeps these bytes and
    /// sends them back with Restore Screen, where they replay like any record.
    /// </summary>
    public byte[] ToDataStream(Func<char, byte> toEbcdic)
    {
        var o = new List<byte>(_cells.Length + 256)
        {
            0x04, 0x40,                    // Clear Unit
            0x04, 0x11, 0x00, 0x00,        // Write to Display, no control actions
            0x11, 0x01, 0x01,              // SBA row 1, col 1
        };
        for (int i = 0; i < _cells.Length; i++)
        {
            var cell = _cells[i];
            if (!cell.IsAttributeByte)
            {
                // Anything that would encode below 0x40 would read back as an
                // order or attribute byte; blank it instead.
                var b = cell.Char == ' ' ? (byte)0x40 : toEbcdic(cell.Char);
                o.Add(b < 0x40 ? (byte)0x40 : b);
                continue;
            }
            if (_attrMarks.TryGetValue(i, out var mark) && mark.FromSf && mark.Length > 0)
            {
                o.Add(0x1D);
                if ((mark.Ffw1 & 0xC0) == 0x40)
                {
                    var ffw1 = _operatorModified.Contains(i + 1) ? (byte)(mark.Ffw1 | 0x08) : mark.Ffw1;
                    o.Add(ffw1);
                    o.Add(mark.Ffw2);
                }
                o.Add(cell.Attribute);
                o.Add((byte)(mark.Length >> 8));
                o.Add((byte)mark.Length);
            }
            else o.Add(cell.Attribute);
        }
        o.Add(0x13);                       // IC: put the cursor back
        o.Add((byte)(CursorRow + 1));
        o.Add((byte)(CursorCol + 1));
        return o.ToArray();
    }

    /// <summary>Immutable copy of everything a renderer needs, safe to hand to another thread.</summary>
    public ScreenSnapshot TakeSnapshot(bool keyboardLocked = false)
        => new(Rows, Cols, CursorRow, CursorCol, Snapshot(), _fields.ToArray(),
            _fields.Where(IsModified).Select(f => f.StartIndex(Cols)).ToHashSet(), keyboardLocked);

    public ScreenCell[] Snapshot()
    {
        var copy = new ScreenCell[_cells.Length];
        Array.Copy(_cells, copy, _cells.Length);
        return copy;
    }
}
