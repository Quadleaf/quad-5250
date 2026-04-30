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
    /// <summary>Bit 2 of the attribute byte distinguishes input from protected.</summary>
    public bool IsInput => (Attribute & 0x04) != 0;

    /// <summary>Lowest 3 bits == 7 means non-display (password fields).</summary>
    public bool IsNonDisplay => (Attribute & 0x07) == 0x07;

    /// <summary>FFW1 bit 7 = bypass: cursor can't enter this field.</summary>
    public bool IsBypass => (Ffw1 & 0x80) != 0;

    /// <summary>FFW1 bit 4 = numeric only.</summary>
    public bool IsNumeric => (Ffw1 & 0x10) != 0;

    /// <summary>FFW1 bit 0 = auto-enter (Enter sent on field fill).</summary>
    public bool IsAutoEnter => (Ffw1 & 0x01) != 0;
}
