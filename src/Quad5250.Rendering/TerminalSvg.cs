using System.Globalization;
using System.Security;
using System.Text;
using System.Windows.Media;
using Quad5250.Protocol.Screen;

namespace Quad5250.Rendering;

/// <summary>
/// Renders a screen as SVG: sharp at any zoom and the text stays
/// selectable, which suits specs and documentation. Uses the same cell
/// layout as the PNG renderer, so masking and redaction are identical.
/// </summary>
public static class TerminalSvg
{
    private const string FontFamily = "'Cascadia Mono', Consolas, 'Courier New', monospace";

    public static string Render(ScreenSnapshot screen, ScreenshotOptions? options = null)
    {
        var o = options ?? new ScreenshotOptions();
        var cellW = o.FontSize * 0.6;
        var cellH = Math.Ceiling(o.FontSize * 1.32);
        var pad = o.Padding;
        var width = screen.Cols * cellW + pad * 2;
        var height = screen.Rows * cellH + pad * 2;
        var rows = CellLayout.Resolve(screen, o.Theme, o.ShowCursor, o.Redactions);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" viewBox=\"0 0 {F(width)} {F(height)}\">\n");
        sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"100%\" height=\"100%\" fill=\"{Hex(o.Theme.Background)}\"/>\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"<g font-family=\"{FontFamily}\" font-size=\"{F(o.FontSize)}\" xml:space=\"preserve\" transform=\"translate({F(pad)} {F(pad)})\">\n");

        for (int r = 0; r < rows.Length; r++)
        {
            var cells = rows[r];
            var top = r * cellH;

            foreach (var (start, end) in Runs(cells, (a, b) => a.Bg == b.Bg))
                if (cells[start].Bg != o.Theme.Background)
                    sb.Append(CultureInfo.InvariantCulture,
                        $"<rect x=\"{F(start * cellW)}\" y=\"{F(top)}\" width=\"{F((end - start) * cellW)}\" height=\"{F(cellH)}\" fill=\"{Hex(cells[start].Bg)}\"/>\n");

            foreach (var (start, end) in Runs(cells, (a, b) => a.Fg == b.Fg))
            {
                var text = new string(cells[start..end].Select(c => c.Ch).ToArray());
                var trimmed = text.TrimEnd(' ');
                var lead = trimmed.Length - trimmed.TrimStart(' ').Length;
                trimmed = trimmed.TrimStart(' ');
                if (trimmed.Length == 0) continue;
                sb.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{F((start + lead) * cellW)}\" y=\"{F(top + cellH * 0.78)}\" textLength=\"{F(trimmed.Length * cellW)}\" lengthAdjust=\"spacingAndGlyphs\" fill=\"{Hex(cells[start].Fg)}\">{SecurityElement.Escape(trimmed)}</text>\n");
            }

            var y = top + cellH - 1.5;
            foreach (var (start, end) in Runs(cells, (a, b) => a.Underline == b.Underline && a.Fg == b.Fg))
                if (cells[start].Underline)
                    sb.Append(CultureInfo.InvariantCulture,
                        $"<line x1=\"{F(start * cellW)}\" y1=\"{F(y)}\" x2=\"{F(end * cellW)}\" y2=\"{F(y)}\" stroke=\"{Hex(cells[start].Fg)}\" stroke-width=\"1\"/>\n");

            foreach (var (start, end) in Runs(cells, (a, b) => a.Redacted == b.Redacted))
                if (cells[start].Redacted)
                    sb.Append(CultureInfo.InvariantCulture,
                        $"<rect x=\"{F(start * cellW)}\" y=\"{F(top + cellH * 0.15)}\" width=\"{F((end - start) * cellW)}\" height=\"{F(cellH * 0.7)}\" fill=\"{Hex(o.Theme.Foreground)}\" fill-opacity=\"0.6\"/>\n");
        }

        sb.Append("</g>\n</svg>\n");
        return sb.ToString();
    }

    /// <summary>Maximal runs [start, end) of cells that match their neighbor.</summary>
    private static IEnumerable<(int Start, int End)> Runs(RenderCell[] cells, Func<RenderCell, RenderCell, bool> same)
    {
        int c = 0;
        while (c < cells.Length)
        {
            int start = c++;
            while (c < cells.Length && same(cells[start], cells[c])) c++;
            yield return (start, c);
        }
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}
