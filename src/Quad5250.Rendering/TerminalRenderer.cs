using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Quad5250.Protocol.Screen;

namespace Quad5250.Rendering;

/// <summary>A run of cells to black out in a rendering, e.g. a patient name in a documentation screenshot.</summary>
public readonly record struct CellRange(int Row, int Col, int Length);

/// <summary>
/// Draws a <see cref="ScreenSnapshot"/> into a WPF <see cref="DrawingContext"/>.
/// The same code backs the desktop terminal control and off-screen
/// screenshots, so a saved image matches what the operator saw.
/// </summary>
public sealed class TerminalRenderer
{
    public static readonly FontFamily DefaultFontFamily = new("Cascadia Mono, Consolas, Courier New");

    private const char PasswordBullet = '•';

    private readonly Typeface _typeface;
    private readonly Dictionary<Color, SolidColorBrush> _brushes = new();
    private readonly Dictionary<Color, Pen> _pens = new();

    public double FontSize { get; }
    public double CellWidth { get; }
    public double CellHeight { get; }

    public TerminalRenderer(double fontSize = 16, FontFamily? fontFamily = null)
    {
        FontSize = fontSize;
        var family = fontFamily ?? DefaultFontFamily;
        _typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var probe = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface, fontSize, Brushes.White, pixelsPerDip: 1.0);
        CellWidth = probe.WidthIncludingTrailingWhitespace;
        CellHeight = Math.Ceiling(family.LineSpacing * fontSize);
    }

    public Size GridSize(int rows, int cols) => new(cols * CellWidth, rows * CellHeight);

    /// <summary>Cell under a point in grid coordinates, or false when outside the grid.</summary>
    public bool TryHitCell(Point p, int rows, int cols, out int row, out int col)
    {
        row = (int)Math.Floor(p.Y / CellHeight);
        col = (int)Math.Floor(p.X / CellWidth);
        return row >= 0 && row < rows && col >= 0 && col < cols;
    }

    public void Render(
        DrawingContext dc,
        ScreenSnapshot screen,
        TerminalTheme theme,
        double pixelsPerDip,
        bool showCursor = true,
        IReadOnlyCollection<CellRange>? redactions = null)
    {
        dc.DrawRectangle(Brush(theme.Background), null, new Rect(GridSize(screen.Rows, screen.Cols)));

        var redacted = RedactedCells(screen, redactions);
        var inputCells = new bool[screen.Rows * screen.Cols];
        foreach (var f in screen.Fields.Where(f => f.IsInput))
        {
            var start = f.StartIndex(screen.Cols);
            for (int i = 0; i < f.Length; i++) inputCells[start + i] = true;
        }

        var cells = new RenderCell[screen.Cols];
        for (int r = 0; r < screen.Rows; r++)
        {
            for (int c = 0; c < screen.Cols; c++)
                cells[c] = Resolve(screen, theme, r, c, inputCells, redacted, showCursor);

            DrawBackgrounds(dc, cells, r, theme.Background);
            DrawText(dc, cells, r, pixelsPerDip);
            DrawUnderlines(dc, cells, r);
            DrawRedactions(dc, cells, r, theme);
        }
    }

    private readonly record struct RenderCell(char Ch, Color Fg, Color Bg, bool Underline, bool Redacted);

    private static RenderCell Resolve(ScreenSnapshot screen, TerminalTheme theme, int r, int c,
        bool[] inputCells, bool[]? redacted, bool showCursor)
    {
        var index = r * screen.Cols + c;
        var cell = screen.Cells[index];
        var style = AttrStyle.For(cell.Attribute, theme);
        var isInput = inputCells[index];
        var hidden = screen.IsHidden(r, c);

        if (redacted?[index] == true)
            return new RenderCell(' ', theme.Foreground, theme.Background, false, true);

        var ch = cell.Char;
        var fg = style.Foreground;
        if (hidden)
        {
            // Non-display input (passwords): show a bullet per typed character
            // so the operator sees something landed, never the text itself.
            // Hidden output text gets no glyph at all.
            ch = isInput && ch != ' ' ? PasswordBullet : ' ';
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

    private void DrawBackgrounds(DrawingContext dc, RenderCell[] cells, int row, Color terminalBg)
    {
        int c = 0;
        while (c < cells.Length)
        {
            var bg = cells[c].Bg;
            int start = c;
            while (c < cells.Length && cells[c].Bg == bg) c++;
            if (bg == terminalBg) continue;
            dc.DrawRectangle(Brush(bg), null, CellRect(row, start, c - start));
        }
    }

    private void DrawText(DrawingContext dc, RenderCell[] cells, int row, double pixelsPerDip)
    {
        // Each run of same-colored cells is drawn at its own column origin so
        // a glyph whose advance differs from the cell width can't drift the
        // rest of the row out of alignment.
        int c = 0;
        var sb = new StringBuilder(cells.Length);
        while (c < cells.Length)
        {
            var fg = cells[c].Fg;
            int start = c;
            sb.Clear();
            while (c < cells.Length && cells[c].Fg == fg) sb.Append(cells[c++].Ch);

            var text = sb.ToString();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var leading = text.Length - text.TrimStart(' ').Length;
            var ft = new FormattedText(text.Trim(' '), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                _typeface, FontSize, Brush(fg), pixelsPerDip);
            dc.DrawText(ft, new Point((start + leading) * CellWidth, row * CellHeight));
        }
    }

    private void DrawUnderlines(DrawingContext dc, RenderCell[] cells, int row)
    {
        var y = row * CellHeight + CellHeight - 1.5;
        int c = 0;
        while (c < cells.Length)
        {
            if (!cells[c].Underline) { c++; continue; }
            var fg = cells[c].Fg;
            int start = c;
            while (c < cells.Length && cells[c].Underline && cells[c].Fg == fg) c++;
            dc.DrawLine(Pen(fg), new Point(start * CellWidth, y), new Point(c * CellWidth, y));
        }
    }

    private void DrawRedactions(DrawingContext dc, RenderCell[] cells, int row, TerminalTheme theme)
    {
        var block = Color.FromArgb(0x99, theme.Foreground.R, theme.Foreground.G, theme.Foreground.B);
        int c = 0;
        while (c < cells.Length)
        {
            if (!cells[c].Redacted) { c++; continue; }
            int start = c;
            while (c < cells.Length && cells[c].Redacted) c++;
            var rect = CellRect(row, start, c - start);
            rect.Inflate(0, -CellHeight * 0.15);
            dc.DrawRectangle(Brush(block), null, rect);
        }
    }

    private Rect CellRect(int row, int col, int count)
        => new(col * CellWidth, row * CellHeight, count * CellWidth, CellHeight);

    private SolidColorBrush Brush(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushes[color] = brush;
        }
        return brush;
    }

    private Pen Pen(Color color)
    {
        if (!_pens.TryGetValue(color, out var pen))
        {
            pen = new Pen(Brush(color), 1.0);
            pen.Freeze();
            _pens[color] = pen;
        }
        return pen;
    }
}
