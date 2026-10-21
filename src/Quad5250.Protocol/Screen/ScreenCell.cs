namespace Quad5250.Protocol.Screen;

/// <summary>
/// One character position on the screen: the rendered glyph plus its
/// 5250 character attribute byte. The attribute byte encodes color,
/// intensity, blink, reverse, underscore, non-display, and column separator.
/// IsAttributeByte marks the cell occupied by the attribute byte itself
/// (the position before each field); it displays blank, never underscored.
/// </summary>
public readonly record struct ScreenCell(char Char, byte Attribute, bool IsAttributeByte = false)
{
    public static ScreenCell Empty => new(' ', 0x20);
}
