using System.Text.RegularExpressions;
using Quad5250.Protocol.DataStream;

namespace Quad5250.Protocol.Screen;

/// <summary>An "F3=Exit" style legend on the screen.</summary>
public sealed record FunctionKeyLabel(Aid Aid, int Row, int StartCol, int EndCol, string Text);

public static partial class FunctionKeyLabels
{
    private const int LegendRows = 5;

    /// <summary>
    /// Find F-key legends in the bottom rows. Each label runs from the F to
    /// the next double space (or end of row), so "F3=Exit" is one label and
    /// "F13=Information Assistant" stays whole despite its inner space.
    /// EndCol is exclusive.
    /// </summary>
    public static IReadOnlyList<FunctionKeyLabel> Find(ScreenSnapshot screen)
    {
        var labels = new List<FunctionKeyLabel>();
        for (int r = Math.Max(0, screen.Rows - LegendRows); r < screen.Rows; r++)
        {
            var rowText = screen.RowText(r);
            foreach (Match m in FKey().Matches(rowText))
            {
                var n = int.Parse(m.Groups[1].Value);
                if (n < 1 || n > 24) continue;
                var end = m.Index + m.Length;
                while (end < rowText.Length && !(rowText[end] == ' ' && end + 1 < rowText.Length && rowText[end + 1] == ' '))
                    end++;
                labels.Add(new FunctionKeyLabel(
                    Enum.Parse<Aid>($"F{n}"), r, m.Index, end, rowText[m.Index..end].TrimEnd()));
            }
        }
        return labels;
    }

    [GeneratedRegex(@"\bF(\d{1,2})\b")]
    private static partial Regex FKey();
}
