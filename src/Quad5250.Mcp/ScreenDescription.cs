using System.Text;
using System.Text.RegularExpressions;
using Quad5250.Protocol.Screen;

namespace Quad5250.Mcp;

/// <summary>An input field as the model sees it: a stable id for this screen plus an inferred label.</summary>
public sealed record FieldInfo(int Id, FieldDefinition Field, string Label);

/// <summary>
/// Text rendering of a screen for the model: numbered rows, the input
/// fields with ids and labels, and the F-key legend. Rows and columns are
/// 1-based, matching how the host and IBM documentation number them.
/// Non-display fields (passwords) never show their content.
/// </summary>
public static partial class ScreenDescription
{
    public static IReadOnlyList<FieldInfo> Fields(ScreenSnapshot screen)
        => screen.Fields
            .Where(f => f.IsInput)
            .Select((f, i) => new FieldInfo(i + 1, f, LabelFor(screen, f)))
            .ToList();

    public static string Describe(ScreenSnapshot screen, string? status = null)
    {
        var sb = new StringBuilder();
        if (status is not null) sb.AppendLine(status);
        sb.AppendLine($"Screen {screen.Rows}x{screen.Cols} | cursor row {screen.CursorRow + 1} col {screen.CursorCol + 1} | keyboard {(screen.KeyboardLocked ? "LOCKED (host busy)" : "ready")}");
        sb.AppendLine();

        for (int r = 0; r < screen.Rows; r++)
            sb.AppendLine($"{r + 1,2}| {screen.RowText(r).TrimEnd()}");

        var fields = Fields(screen);
        sb.AppendLine();
        if (fields.Count == 0) sb.AppendLine("Input fields: none");
        else
        {
            sb.AppendLine("Input fields (fill by id or label):");
            foreach (var info in fields) sb.AppendLine("  " + FieldLine(screen, info));
        }

        var keys = FunctionKeyLabels.Find(screen);
        if (keys.Count > 0)
            sb.AppendLine().AppendLine("Function keys shown: " + string.Join(", ", keys.Select(k => k.Text)));
        return sb.ToString();
    }

    private static string FieldLine(ScreenSnapshot screen, FieldInfo info)
    {
        var f = info.Field;
        var flags = new List<string>();
        if (f.IsBypass) flags.Add("protected");
        if (f.IsNumeric) flags.Add(f.Shift.ToString());
        if (f.IsMonocase) flags.Add("uppercase");
        if (f.IsMandatoryEnter) flags.Add("required");
        if (f.IsAutoEnter) flags.Add("auto-enter");
        if (screen.IsModified(f)) flags.Add("modified");

        var value = f.IsNonDisplay
            ? $"(hidden, {FieldValue(screen, f).Length} chars)"
            : $"\"{FieldValue(screen, f)}\"";
        var label = info.Label.Length > 0 ? $" \"{info.Label}\"" : "";
        var flagText = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
        return $"[{info.Id}]{label} row {f.Row + 1} col {f.Col + 1} len {f.Length} = {value}{flagText}";
    }

    public static string FieldValue(ScreenSnapshot screen, FieldDefinition f)
    {
        var start = f.StartIndex(screen.Cols);
        var chars = new char[f.Length];
        for (int i = 0; i < f.Length; i++) chars[i] = screen.Cells[start + i].Char;
        return new string(chars).TrimEnd(' ', '\0');
    }

    /// <summary>
    /// The prompt text to the left of a field on its row, without the dot
    /// leaders ("User  . . . . ." → "User"). Empty when there isn't one,
    /// e.g. subfile option columns.
    /// </summary>
    public static string LabelFor(ScreenSnapshot screen, FieldDefinition f)
    {
        // Text before the attribute byte, from the end of the previous field on the row.
        var rowText = screen.RowText(f.Row);
        var end = Math.Max(0, f.Col - 1);
        var start = screen.Fields
            .Where(o => o.Row == f.Row && o.Col < f.Col)
            .Select(o => Math.Min(o.Col + o.Length, end))
            .DefaultIfEmpty(0)
            .Max();
        var text = rowText[start..end];

        var segment = WideGap().Split(text.Trim()).LastOrDefault() ?? "";
        // A field inside a host-drawn window has the window's left border
        // (":" or ".") before its prompt: ":  Select Confirmed/Unconfirmed/All".
        segment = WindowBorder().Replace(segment, "");
        return Leader().Replace(segment, "").Trim();
    }

    [GeneratedRegex(@"^[:.]+\s+")]
    private static partial Regex WindowBorder();

    [GeneratedRegex(@"\s{3,}")]
    private static partial Regex WideGap();

    // Trailing dot leaders and colons: "User . . . . :" → "User", "===>" stays.
    [GeneratedRegex(@"(\s*[.:])+\s*$")]
    private static partial Regex Leader();
}
