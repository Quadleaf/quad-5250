namespace Quad5250.Protocol.Screen;

/// <summary>
/// One input field on the screen, defined by an SF (Start of Field) order.
/// Position is the first editable cell — the leading attribute byte sits at
/// (Row, Col-1). Length is the number of editable cells.
/// </summary>
public sealed record FieldDefinition(
    int Row,
    int Col,
    int Length,
    byte Attribute,
    byte Ffw1,
    byte Ffw2)
{
    /// <summary>
    /// Always true: a <see cref="FieldDefinition"/> is only created from a
    /// Start-of-Field order, which by definition declares an input field.
    /// (The attribute byte's bit 2 is informational about color-style only —
    /// SF orders that draw input fields with attr 0x20 like the Main Menu
    /// command line are still input fields.)
    /// </summary>
    public bool IsInput => true;

    /// <summary>Lowest 3 bits == 7 means non-display (password fields).</summary>
    public bool IsNonDisplay => (Attribute & 0x07) == 0x07;

    /// <summary>FFW1 bit 7 = bypass: cursor can't enter this field.</summary>
    public bool IsBypass => (Ffw1 & 0x80) != 0;

    /// <summary>FFW1 bit 4 = numeric only.</summary>
    public bool IsNumeric => (Ffw1 & 0x10) != 0;

    /// <summary>FFW1 bit 0 = auto-enter (Enter sent on field fill).</summary>
    public bool IsAutoEnter => (Ffw1 & 0x01) != 0;
}
