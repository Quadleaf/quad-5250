namespace Quad5250.Protocol.Screen;

/// <summary>
/// One field on the screen, defined by an SF (Start of Field) order.
/// Position is the first cell after the leading attribute byte, which sits at
/// (Row, Col-1). Length is the number of cells; a field may wrap onto the
/// next row, so treat it as a linear range of buffer addresses.
/// </summary>
/// <remarks>
/// Field Format Word bits per IBM 5494 Functions Reference (bit 0 = MSB):
/// FFW1: 0-1 = 01 (marks the FFW as present), 2 = bypass, 3 = dup enable,
/// 4 = modified data tag, 5-7 = field shift/edit specification.
/// FFW2: 0 = auto enter, 1 = field exit required, 2 = monocase,
/// 4 = mandatory enter, 5-7 = right adjust / mandatory fill.
/// An SF without an FFW defines an output-only field; Ffw1/Ffw2 are 0 then.
/// </remarks>
public sealed record FieldDefinition(
    int Row,
    int Col,
    int Length,
    byte Attribute,
    byte Ffw1,
    byte Ffw2)
{
    /// <summary>True when the SF carried an FFW, i.e. the field accepts input.</summary>
    public bool IsInput => (Ffw1 & 0xC0) == 0x40;

    /// <summary>Lowest 3 bits == 7 means non-display (password fields).</summary>
    public bool IsNonDisplay => (Attribute & 0x07) == 0x07;

    /// <summary>FFW1 bit 2 = bypass: protected, the cursor can't type into it.</summary>
    public bool IsBypass => (Ffw1 & 0x20) != 0;

    /// <summary>FFW1 bit 3 = Dup key allowed.</summary>
    public bool IsDupEnabled => (Ffw1 & 0x10) != 0;

    /// <summary>FFW1 bit 4 = MDT set by the host: field is sent back on Read MDT even if untouched.</summary>
    public bool IsHostModified => (Ffw1 & 0x08) != 0;

    public FieldShift Shift => (FieldShift)(Ffw1 & 0x07);

    /// <summary>True for shift types that restrict entry to numeric characters.</summary>
    public bool IsNumeric => Shift is FieldShift.NumericOnly or FieldShift.DigitsOnly or FieldShift.SignedNumeric;

    /// <summary>FFW2 bit 0 = auto enter: filling the last position sends Enter.</summary>
    public bool IsAutoEnter => (Ffw2 & 0x80) != 0;

    /// <summary>FFW2 bit 1 = field exit required before leaving the field.</summary>
    public bool IsFieldExitRequired => (Ffw2 & 0x40) != 0;

    /// <summary>FFW2 bit 2 = monocase: letters are upper-cased as typed.</summary>
    public bool IsMonocase => (Ffw2 & 0x20) != 0;

    /// <summary>FFW2 bit 4 = mandatory enter: something must be typed before an AID key.</summary>
    public bool IsMandatoryEnter => (Ffw2 & 0x08) != 0;

    /// <summary>Operator can type here: an input field that isn't bypass.</summary>
    public bool IsEnterable => IsInput && !IsBypass;

    /// <summary>Linear buffer address of the first cell, given the screen width.</summary>
    public int StartIndex(int cols) => Row * cols + Col;

    public bool Contains(int index, int cols)
    {
        var start = StartIndex(cols);
        return index >= start && index < start + Length;
    }
}

/// <summary>FFW1 bits 5-7: which characters the operator may enter.</summary>
public enum FieldShift : byte
{
    AlphaShift = 0,
    AlphaOnly = 1,
    NumericShift = 2,
    NumericOnly = 3,
    Katakana = 4,
    DigitsOnly = 5,
    IoFeature = 6,
    SignedNumeric = 7,
}
