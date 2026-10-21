using System.Windows.Media;
using Quad5250.Protocol.Screen;

namespace Quad5250.Rendering;

/// <summary>One screen cell as it should be drawn, after attributes, masking, cursor and redaction.</summary>
internal readonly record struct RenderCell(char Ch, Color Fg, Color Bg, bool Underline, bool Redacted);

/// <summary>
/// Decides how every cell draws. Shared by the WPF and SVG renderers so a
/// PNG and an SVG of the same screen agree, including what gets hidden.
/// </summary>
internal static class CellLayout
{
    private const char PasswordBullet = '•';

    public static RenderCell[][] Resolve(ScreenSnapshot screen, TerminalTheme theme, bool showCursor,
        IReadOnlyCollection<CellRange>? redactions)
    {
        var redacted = RedactedCells(screen, redactions);
        var inputCells = new bool[screen.Rows * screen.Cols];
        foreach (var f in screen.Fields.Where(f => f.IsInput))
        {
            var start = f.StartIndex(screen.Cols);
            for (int i = 0; i < f.Length; i++) inputCells[start + i] = true;
        }

        var rows = new RenderCell[screen.Rows][];
        for (int r = 0; r < screen.Rows; r++)
        {
            rows[r] = new RenderCell[screen.Cols];
            for (int c = 0; c < screen.Cols; c++)
                rows[r][c] = Resolve(screen, theme, r, c, inputCells, redacted, showCursor);
        }
        return rows;
    }

    private static RenderCell Resolve(ScreenSnapshot screen, TerminalTheme theme, int r, int c,
        bool[] inputCells, bool[]? redacted, bool showCursor)
    {
        var index = r * screen.Cols + c;
        var cell = screen.Cells[index];
        var style = AttrStyle.For(cell.Attribute, theme);

        if (redacted?[index] == true)
            return new RenderCell(' ', theme.Foreground, theme.Background, false, true);

        var ch = cell.Char;
        var fg = style.Foreground;
        if (screen.IsHidden(r, c))
        {
            // Non-display input (passwords): show a bullet per typed character
            // so the operator sees something landed, never the text itself.
            // Hidden output text gets no glyph at all.
            ch = inputCells[index] && ch != ' ' ? PasswordBullet : ' ';
            fg = theme.Foreground;
        }

        var bg = style.Background;
        if (showCursor && r == screen.CursorRow && c == screen.CursorCol)
        {
            bg = theme.Cursor;
            fg = theme.Background;
        }

        // The attribute byte's own cell starts the field but isn't part of it:
        // underscoring it would put a stray mark just before every input field.
        return new RenderCell(ch, fg, bg, style.Underline && !cell.IsAttributeByte, false);
    }

    private static bool[]? RedactedCells(ScreenSnapshot screen, IReadOnlyCollection<CellRange>? redactions)
    {
        if (redactions is null || redactions.Count == 0) return null;
        var total = screen.Rows * screen.Cols;
        var mask = new bool[total];
        foreach (var range in redactions)
        {
            var start = range.Row * screen.Cols + range.Col;
            for (int i = Math.Max(0, start); i < Math.Min(total, start + range.Length); i++) mask[i] = true;
        }
        return mask;
    }
}
