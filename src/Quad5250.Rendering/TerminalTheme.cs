using System.Windows.Media;

namespace Quad5250.Rendering;

/// <summary>
/// Terminal color scheme. Mirrors the web client's themes (attrColor.ts):
/// the monochrome attributes 0x20-0x27 follow the theme, the colored ones
/// stay constant so red/blue keep their meaning.
/// </summary>
public sealed record TerminalTheme(string Name, Color Background, Color Foreground, Color Highlight, Color Cursor)
{
    public static TerminalTheme Green { get; } = new("Green", Rgb(0x000000), Rgb(0x2bef2b), Rgb(0xe8e8e8), Rgb(0x2bef2b));
    public static TerminalTheme Dark { get; } = new("Dark", Rgb(0x000000), Rgb(0xe8e8e8), Rgb(0xffffff), Rgb(0xe8e8e8));
    public static TerminalTheme Light { get; } = new("Light", Rgb(0xffffff), Rgb(0x1a1a1a), Rgb(0x000000), Rgb(0x1a1a1a));

    public static IReadOnlyList<TerminalTheme> All { get; } = [Green, Dark, Light];

    public static TerminalTheme FromName(string? name)
        => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Green;

    internal static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

/// <summary>How one 5250 attribute byte draws.</summary>
public readonly record struct AttrStyle(Color Foreground, Color Background, bool Underline, bool Blink, bool Hidden)
{
    private static readonly Color Red = TerminalTheme.Rgb(0xef4d4d);
    private static readonly Color Cyan = TerminalTheme.Rgb(0x33d6d6);
    private static readonly Color Yellow = TerminalTheme.Rgb(0xe6c84a);
    private static readonly Color Magenta = TerminalTheme.Rgb(0xd678d6);
    private static readonly Color Blue = TerminalTheme.Rgb(0x4d89ef);

    /// <summary>
    /// Attribute byte → style. Layout from tn5250j's ATTR_32 … ATTR_63.
    /// Underlines come only from the attribute, as on a real 5250: input
    /// fields are usually 0x24 (underscored), but a non-display field such
    /// as the Sign On password (0x27) shows nothing at all.
    /// </summary>
    public static AttrStyle For(byte attr, TerminalTheme theme)
    {
        var fg = theme.Foreground;
        var bg = theme.Background;
        var hi = theme.Highlight;

        return attr switch
        {
            0x20 => new(fg, bg, false, false, false),
            0x21 => new(bg, fg, false, false, false),
            0x22 => new(hi, bg, false, false, false),
            0x23 => new(bg, hi, false, false, false),
            0x24 => new(fg, bg, true, false, false),
            0x25 => new(bg, fg, true, false, false),
            0x26 => new(hi, bg, true, false, false),
            0x27 => new(bg, bg, false, false, true),

            0x28 => new(Red, bg, false, false, false),
            0x29 => new(bg, Red, false, false, false),
            0x2a => new(Red, bg, false, true, false),
            0x2b => new(bg, Red, false, true, false),
            0x2c => new(Red, bg, true, false, false),
            0x2d => new(bg, Red, true, false, false),
            0x2e => new(Red, bg, true, true, false),
            0x2f => new(bg, bg, false, false, true),

            0x30 => new(Cyan, bg, false, false, false),
            0x31 => new(bg, Cyan, false, false, false),
            0x32 => new(Yellow, bg, false, false, false),
            0x33 => new(bg, Yellow, false, false, false),
            0x34 => new(Cyan, bg, true, false, false),
            0x35 => new(bg, Cyan, true, false, false),
            0x36 => new(Yellow, bg, true, false, false),
            0x37 => new(bg, bg, false, false, true),

            0x38 => new(Magenta, bg, false, false, false),
            0x39 => new(bg, Magenta, false, false, false),
            0x3a => new(Blue, bg, false, false, false),
            0x3b => new(bg, Blue, false, false, false),
            0x3c => new(Magenta, bg, true, false, false),
            0x3d => new(bg, Magenta, true, false, false),
            0x3e => new(Blue, bg, true, false, false),
            0x3f => new(bg, bg, false, false, true),

            _ => new(fg, bg, false, false, false),
        };
    }
}
