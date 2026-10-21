using Quad5250.Protocol.Screen;
using Quad5250.Protocol.Session;

namespace Quad5250.Protocol.Input;

/// <summary>Outcome of an operator edit.</summary>
public enum EditResult
{
    /// <summary>Applied.</summary>
    Ok,
    /// <summary>Applied, and the field is auto-enter and now full: the caller should send Enter.</summary>
    AutoEnter,
    /// <summary>Cursor isn't in an enterable field.</summary>
    ProtectedPosition,
    /// <summary>Character not allowed by the field's shift (e.g. a letter in a digits-only field).</summary>
    InvalidCharacter,
    /// <summary>Keyboard is locked waiting for the host, or the session is closed.</summary>
    Inhibited,
}

/// <summary>
/// Operator-side 5250 editing: typing, cursor movement between fields, and
/// collecting modified fields for an AID key. Edits go straight into the
/// <see cref="ScreenBuffer"/> the way a real terminal's do, so a partial host
/// write (an error message on row 24) leaves typed input in place, and
/// host-supplied field content is kept when the operator edits part of it.
/// Not thread-safe; <see cref="Tn5250Session"/> serializes access.
/// </summary>
public sealed class FieldEditor
{
    private readonly ScreenBuffer _screen;

    public FieldEditor(ScreenBuffer screen) => _screen = screen;

    /// <summary>Fields on the current screen, for locating one to fill within the same edit.</summary>
    public IReadOnlyList<FieldDefinition> Fields => _screen.Fields;

    private int Cols => _screen.Cols;
    private int CursorIndex => _screen.CursorRow * Cols + _screen.CursorCol;

    private IEnumerable<FieldDefinition> EnterableFields => _screen.Fields.Where(f => f.IsEnterable);

    private FieldDefinition? EnterableFieldAt(int index)
        => EnterableFields.FirstOrDefault(f => f.Contains(index, Cols));

    private void SetCursorIndex(int index) => _screen.SetCursor(index / Cols, index % Cols);

    /// <summary>
    /// After a host write: a screen that didn't position the cursor (left at
    /// 0,0) gets it on the first enterable field, as 5250 terminals do.
    /// </summary>
    public void OnHostWrite()
    {
        if (_screen.CursorRow != 0 || _screen.CursorCol != 0) return;
        if (EnterableFieldAt(0) is not null) return;
        Home();
    }

    public EditResult TypeChar(char ch)
    {
        var index = CursorIndex;
        var field = EnterableFieldAt(index);
        if (field is null) return EditResult.ProtectedPosition;
        if (!TryNormalize(field, ch, out ch)) return EditResult.InvalidCharacter;

        _screen.SetCharAt(index, ch);
        _screen.MarkModified(field);

        var end = field.StartIndex(Cols) + field.Length;
        if (index + 1 < end)
        {
            SetCursorIndex(index + 1);
            return EditResult.Ok;
        }
        if (field.IsAutoEnter) return EditResult.AutoEnter;
        TabForward();
        return EditResult.Ok;
    }

    /// <summary>Destructive backspace within the current field.</summary>
    public EditResult Backspace()
    {
        var index = CursorIndex;
        var field = EnterableFieldAt(index);
        if (field is null) return EditResult.ProtectedPosition;
        if (index == field.StartIndex(Cols)) return EditResult.Ok;

        SetCursorIndex(index - 1);
        _screen.SetCharAt(index - 1, ' ');
        _screen.MarkModified(field);
        return EditResult.Ok;
    }

    /// <summary>Delete the character at the cursor, shifting the rest of the field left.</summary>
    public EditResult Delete()
    {
        var index = CursorIndex;
        var field = EnterableFieldAt(index);
        if (field is null) return EditResult.ProtectedPosition;

        var end = field.StartIndex(Cols) + field.Length;
        for (int i = index; i < end - 1; i++)
            _screen.SetCharAt(i, _screen.CellAt(i + 1).Char);
        _screen.SetCharAt(end - 1, ' ');
        _screen.MarkModified(field);
        return EditResult.Ok;
    }

    /// <summary>Blank from the cursor to the end of the field (Erase EOF).</summary>
    public EditResult EraseToEndOfField()
    {
        var index = CursorIndex;
        var field = EnterableFieldAt(index);
        if (field is null) return EditResult.ProtectedPosition;

        var end = field.StartIndex(Cols) + field.Length;
        for (int i = index; i < end; i++) _screen.SetCharAt(i, ' ');
        _screen.MarkModified(field);
        return EditResult.Ok;
    }

    public void TabForward()
    {
        var index = CursorIndex;
        var fields = EnterableFields.ToList();
        if (fields.Count == 0) return;
        var next = fields.FirstOrDefault(f => f.StartIndex(Cols) > index) ?? fields[0];
        SetCursorIndex(next.StartIndex(Cols));
    }

    public void TabBackward()
    {
        var index = CursorIndex;
        var fields = EnterableFields.ToList();
        if (fields.Count == 0) return;
        // From inside a field, back-tab goes to that field's start first.
        var current = fields.FirstOrDefault(f => f.Contains(index, Cols));
        if (current is not null && index != current.StartIndex(Cols))
        {
            SetCursorIndex(current.StartIndex(Cols));
            return;
        }
        var prev = fields.LastOrDefault(f => f.StartIndex(Cols) < index) ?? fields[^1];
        SetCursorIndex(prev.StartIndex(Cols));
    }

    /// <summary>Cursor to the first enterable field (or 0,0 if there is none).</summary>
    public void Home()
    {
        var first = EnterableFields.FirstOrDefault();
        SetCursorIndex(first?.StartIndex(Cols) ?? 0);
    }

    public void MoveCursor(int deltaRow, int deltaCol)
        => _screen.SetCursor(_screen.CursorRow + deltaRow, _screen.CursorCol + deltaCol);

    public void SetCursor(int row, int col) => _screen.SetCursor(row, col);

    /// <summary>
    /// Replace a field's whole content (programmatic entry: credential fill,
    /// automation). Text longer than the field is rejected rather than cut.
    /// </summary>
    public EditResult FillField(FieldDefinition field, string text)
    {
        if (!field.IsEnterable) return EditResult.ProtectedPosition;
        if (text.Length > field.Length) return EditResult.InvalidCharacter;

        var normalized = new char[text.Length];
        for (int i = 0; i < text.Length; i++)
            if (!TryNormalize(field, text[i], out normalized[i])) return EditResult.InvalidCharacter;

        var start = field.StartIndex(Cols);
        for (int i = 0; i < field.Length; i++)
            _screen.SetCharAt(start + i, i < normalized.Length ? normalized[i] : ' ');
        _screen.MarkModified(field);
        return EditResult.Ok;
    }

    /// <summary>Current content of a field, trailing blanks trimmed.</summary>
    public string ReadField(FieldDefinition field)
    {
        var start = field.StartIndex(Cols);
        var chars = new char[field.Length];
        for (int i = 0; i < field.Length; i++) chars[i] = _screen.CellAt(start + i).Char;
        return new string(chars).TrimEnd(' ', '\0');
    }

    /// <summary>
    /// Fields to return on a Read MDT response: every input field with its
    /// MDT set, carrying the field's full content (host-supplied text plus
    /// operator edits), trailing blanks trimmed — the host treats the rest
    /// of the field as nulls.
    /// </summary>
    public IReadOnlyList<ModifiedField> GetModifiedFields()
        => _screen.Fields
            .Where(f => f.IsInput && _screen.IsModified(f))
            .Select(f => new ModifiedField(f.Row, f.Col, ReadField(f)))
            .ToList();

    private static bool TryNormalize(FieldDefinition field, char ch, out char result)
    {
        result = field.IsMonocase ? char.ToUpperInvariant(ch) : ch;
        if (char.IsControl(result)) return false;
        return field.Shift switch
        {
            FieldShift.DigitsOnly => char.IsAsciiDigit(result),
            FieldShift.SignedNumeric => char.IsAsciiDigit(result) || result is '+' or '-',
            FieldShift.NumericOnly => char.IsAsciiDigit(result) || result is '+' or '-' or ',' or '.' or ' ',
            FieldShift.AlphaOnly => char.IsLetter(result) || result is ',' or '.' or '-' or ' ',
            _ => true,
        };
    }
}
