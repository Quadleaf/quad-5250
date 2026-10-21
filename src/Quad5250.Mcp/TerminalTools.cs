using System.ComponentModel;
using System.IO;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Quad5250.Rendering;

namespace Quad5250.Mcp;

public sealed record Region(
    [property: Description("1-based row.")] int Row,
    [property: Description("1-based starting column.")] int Col,
    [property: Description("Number of cells; may run onto following rows.")] int Length);

[McpServerToolType]
public sealed class TerminalTools(TerminalService terminal)
{
    [McpServerTool(Name = "list_hosts", ReadOnly = true, Idempotent = true)]
    [Description("List the IBM i hosts this server is allowed to connect to.")]
    public string ListHosts()
        => terminal.Settings.Hosts.Count == 0
            ? "No hosts configured (QUAD5250_HOSTS)."
            : string.Join("\n", terminal.Settings.Hosts.Select(h => $"{h.Name}: {h.Host}:{h.Port}"));

    [McpServerTool(Name = "connect", OpenWorld = true)]
    [Description("Open a 5250 session to a configured host (replacing any current one) and return its first screen, normally Sign On.")]
    public Task<string> Connect(
        [Description("Host name from list_hosts, e.g. \"dev\".")] string host,
        CancellationToken ct)
        => terminal.RunAsync(() => terminal.ConnectAsync(host, ct), ct);

    [McpServerTool(Name = "sign_on", Destructive = false, OpenWorld = true)]
    [Description("Sign on with the configured user and password (the password never passes through you). Only works on a sign-on screen. One attempt per host: a rejected sign-on blocks further attempts until the server restarts, to avoid locking the profile.")]
    public Task<string> SignOn(
        [Description("Seconds to wait for the host after Enter.")] int timeoutSeconds = 30,
        CancellationToken ct = default)
        => terminal.RunAsync(() => terminal.SignOnAsync(TimeSpan.FromSeconds(timeoutSeconds), ct), ct);

    [McpServerTool(Name = "read_screen", ReadOnly = true, Idempotent = true)]
    [Description("Return the current screen as numbered text rows, the input fields (id, label, position, value, flags) and the function keys shown.")]
    public Task<string> ReadScreen(CancellationToken ct)
        => terminal.RunAsync(() => Task.FromResult(terminal.Describe()), ct);

    [McpServerTool(Name = "wait_for_screen", ReadOnly = true)]
    [Description("Wait for the host to finish a screen (keyboard released and output quiet), then return it. Use after press_key reports the host is still busy.")]
    public Task<string> WaitForScreen(
        [Description("Seconds to wait.")] int timeoutSeconds = 30,
        CancellationToken ct = default)
        => terminal.RunAsync(() => terminal.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), ct), ct);

    [McpServerTool(Name = "fill_fields", Destructive = false)]
    [Description("Type values into input fields on the current screen. Each value replaces the field's whole content. Nothing reaches the host until press_key.")]
    public Task<string> FillFields(
        [Description("Fields to fill, by id or label from read_screen.")] FieldValue[] fields,
        CancellationToken ct)
        => terminal.RunAsync(() => Task.FromResult(terminal.FillFields(fields)), ct);

    [McpServerTool(Name = "press_key", Destructive = true, OpenWorld = true)]
    [Description("Press an AID key, sending modified fields to the host, wait for the next screen and return it. This acts on the real system: Enter on a maintenance screen saves data. Keys: Enter, F1-F24, PageUp, PageDown, Clear, Help, Print. Optionally place the cursor first, e.g. on a field before F4=Prompt or on a list line.")]
    public Task<string> PressKey(
        [Description("Key name.")] string key,
        [Description("1-based row to put the cursor on before pressing.")] int? cursorRow = null,
        [Description("1-based column to put the cursor on before pressing.")] int? cursorCol = null,
        [Description("Seconds to wait for the host's reply.")] int timeoutSeconds = 30,
        CancellationToken ct = default)
        => terminal.RunAsync(() => terminal.PressKeyAsync(key, cursorRow, cursorCol, TimeSpan.FromSeconds(timeoutSeconds), ct), ct);

    [McpServerTool(Name = "screenshot")]
    [Description("Render the current screen as an image for specs and documentation, save it, and return it. Password fields are always blank. Screens can contain patient data: redact it (by field id or region) before the image goes anywhere shared, and keep unredacted images out of git repositories.")]
    public Task<CallToolResult> Screenshot(
        [Description("\"png\" (returned inline and saved) or \"svg\" (saved; sharp at any zoom, selectable text).")] string format = "png",
        [Description("File path to save to. Relative paths go under the screenshot folder; default is a timestamped name there.")] string? path = null,
        [Description("Input field ids to black out.")] int[]? redactFields = null,
        [Description("Screen regions to black out, e.g. a patient name shown as output text.")] Region[]? redactRegions = null,
        [Description("\"Green\", \"Dark\" or \"Light\". Light prints best in documents.")] string theme = "Green",
        [Description("Draw the cursor block.")] bool showCursor = false,
        CancellationToken ct = default)
        => terminal.RunAsync(() => Task.FromResult(RenderScreenshot(format, path, redactFields, redactRegions, theme, showCursor)), ct);

    [McpServerTool(Name = "disconnect", Destructive = false)]
    [Description("Close the 5250 session. Prefer signing off on the host first (e.g. SIGNOFF on a command line) so the job ends cleanly.")]
    public Task<string> Disconnect(CancellationToken ct)
        => terminal.RunAsync(terminal.DisconnectAsync, ct);

    private CallToolResult RenderScreenshot(string format, string? path, int[]? redactFields, Region[]? redactRegions, string theme, bool showCursor)
    {
        var fmt = format.Trim().ToLowerInvariant();
        if (fmt is not ("png" or "svg")) throw new McpException("format must be \"png\" or \"svg\".");

        var snap = terminal.Snapshot();
        var fields = ScreenDescription.Fields(snap);
        var redactions = new List<CellRange>();
        foreach (var id in redactFields ?? [])
        {
            var info = fields.FirstOrDefault(f => f.Id == id) ?? throw new McpException($"No input field [{id}] to redact.");
            redactions.Add(new CellRange(info.Field.Row, info.Field.Col, info.Field.Length));
        }
        foreach (var r in redactRegions ?? [])
        {
            if (r.Row < 1 || r.Row > snap.Rows || r.Col < 1 || r.Col > snap.Cols || r.Length < 1)
                throw new McpException($"Region row {r.Row} col {r.Col} len {r.Length} is off the screen.");
            redactions.Add(new CellRange(r.Row - 1, r.Col - 1, r.Length));
        }

        var options = new ScreenshotOptions
        {
            Theme = TerminalTheme.FromName(theme),
            ShowCursor = showCursor,
            Redactions = redactions,
        };

        var file = ResolvePath(path, fmt);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var summary = $"Saved {file}" + (redactions.Count > 0 ? $" ({redactions.Count} region(s) redacted)." : ".");

        if (fmt == "svg")
        {
            File.WriteAllText(file, TerminalSvg.Render(snap, options));
            return new CallToolResult { Content = [new TextContentBlock { Text = summary }] };
        }

        var png = TerminalScreenshot.RenderPng(snap, options);
        File.WriteAllBytes(file, png);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = summary }, ImageContentBlock.FromBytes(png, "image/png")],
        };
    }

    private string ResolvePath(string? path, string ext)
    {
        var dir = terminal.Settings.ScreenshotDir;
        if (string.IsNullOrWhiteSpace(path))
            return Path.Combine(dir, $"5250-{DateTime.Now:yyyyMMdd-HHmmss-fff}.{ext}");
        var full = Path.IsPathRooted(path) ? path : Path.Combine(dir, path);
        return Path.HasExtension(full) ? full : $"{full}.{ext}";
    }
}
