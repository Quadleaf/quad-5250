using System.IO;
using System.Text.Json;
using ModelContextProtocol;
using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Input;
using Quad5250.Protocol.Screen;
using Quad5250.Protocol.Session;

namespace Quad5250.Mcp;

/// <summary>
/// The one 5250 session this server drives. Tool calls are serialized: a
/// terminal has one keyboard. Protocol logging is off on purpose — the
/// client's TX log would include the sign-on record, password and all.
/// </summary>
public sealed class TerminalService(McpSettings settings) : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _gate = new(1, 1);
    // Hosts where sign_on failed. A retry with the same configured password
    // can only fail again and count toward the profile's QMAXSIGN lockout.
    private readonly HashSet<string> _signOnFailed = new(StringComparer.OrdinalIgnoreCase);
    private Tn5250Session? _session;
    private HostEntry? _host;

    public McpSettings Settings => settings;

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await action(); }
        finally { _gate.Release(); }
    }

    // ---- Connection --------------------------------------------------------

    public async Task<string> ConnectAsync(string hostName, CancellationToken ct)
    {
        var host = settings.Hosts.FirstOrDefault(h => h.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException(settings.Hosts.Count == 0
                ? "No hosts are configured. Set QUAD5250_HOSTS in the MCP server config (e.g. dev=10.0.0.5)."
                : $"Unknown host '{hostName}'. Configured: {string.Join(", ", settings.Hosts.Select(h => h.Name))}.");

        await DisconnectAsync();
        var session = new Tn5250Session(host.Host, host.Port);
        try
        {
            await session.StartAsync(ct);
        }
        catch (Exception ex)
        {
            await session.DisposeAsync();
            throw new McpException($"Could not connect to {host.Name}: {ex.Message}");
        }

        _session = session;
        _host = host;
        Audit("connect", new { host = host.Name });

        var ready = await session.WaitForInputReadyAsync(ConnectTimeout, ct: ct);
        return Describe(ready ? $"Connected to {host.Name}." : $"Connected to {host.Name}, but the host hasn't released the keyboard yet.");
    }

    public async Task<string> DisconnectAsync()
    {
        if (_session is null) return "Not connected.";
        var name = _host?.Name;
        await _session.DisposeAsync();
        _session = null;
        _host = null;
        Audit("disconnect", new { host = name });
        return $"Disconnected from {name}.";
    }

    private Tn5250Session Session
        => _session is { IsConnected: true } s
            ? s
            : throw new McpException(_session is null
                ? "Not connected. Call connect first."
                : "The host closed the connection. Call connect to start a new session.");

    // ---- Reading -----------------------------------------------------------

    public ScreenSnapshot Snapshot() => Session.GetSnapshot();

    public string Describe(string? status = null) => ScreenDescription.Describe(Session.GetSnapshot(), status);

    public async Task<string> WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        var ready = await Session.WaitForInputReadyAsync(timeout, ct: ct);
        return Describe(ready ? null : $"Host still busy after {timeout.TotalSeconds:0}s; the screen may be partial.");
    }

    // ---- Input -------------------------------------------------------------

    public string FillFields(IReadOnlyList<FieldValue> values)
    {
        if (values.Count == 0) throw new McpException("Pass at least one field to fill.");
        var session = Session;
        var targets = ScreenDescription.Fields(session.GetSnapshot());
        var resolved = values.Select(v => (Info: Resolve(targets, v.Field), v.Value)).ToList();

        string? failure = null;
        var result = session.Edit(editor =>
        {
            foreach (var (info, value) in resolved)
            {
                // Fields are matched by position: if the host redrew the
                // screen since it was read, the field may not be there now.
                var live = editor.Fields.FirstOrDefault(f => f.Row == info.Field.Row && f.Col == info.Field.Col && f.IsInput);
                if (live is null) { failure = $"Field [{info.Id}] is no longer on the screen; read it again."; return EditResult.ProtectedPosition; }
                var r = editor.FillField(live, value);
                if (r != EditResult.Ok)
                {
                    failure = r switch
                    {
                        EditResult.ProtectedPosition => $"Field [{info.Id}] is protected.",
                        EditResult.InvalidCharacter => value.Length > live.Length
                            ? $"Value for [{info.Id}] is {value.Length} chars; the field holds {live.Length}."
                            : $"Value for [{info.Id}] has characters the field doesn't accept ({live.Shift}).",
                        _ => $"Could not fill [{info.Id}]: {r}.",
                    };
                    return r;
                }
            }
            return EditResult.Ok;
        });

        if (result == EditResult.Inhibited)
            throw new McpException("Keyboard is locked: the host is still processing. Call wait_for_screen first.");
        if (result != EditResult.Ok)
            throw new McpException(failure ?? $"Fill failed: {result}.");

        Audit("fill", new { fields = resolved.Select(r => new { id = r.Info.Id, label = r.Info.Label, length = r.Value.Length }) });
        return Describe($"Filled {resolved.Count} field(s). Nothing is sent until you press a key.");
    }

    private static FieldInfo Resolve(IReadOnlyList<FieldInfo> fields, string field)
    {
        if (int.TryParse(field, out var id))
            return fields.FirstOrDefault(f => f.Id == id)
                ?? throw new McpException($"No input field [{id}] on this screen.");

        var matches = fields.Where(f => f.Label.Equals(field.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new McpException($"No input field labelled '{field}'. Use the field id from read_screen."),
            _ => throw new McpException($"'{field}' matches fields {string.Join(", ", matches.Select(m => m.Id))}; use the id."),
        };
    }

    public async Task<string> PressKeyAsync(string key, int? cursorRow, int? cursorCol, TimeSpan timeout, CancellationToken ct)
    {
        var aid = ParseKey(key);
        var session = Session;
        if ((cursorRow is null) != (cursorCol is null))
            throw new McpException("Pass both cursor_row and cursor_col, or neither.");

        if (cursorRow is { } row && cursorCol is { } col)
        {
            var snap = session.GetSnapshot();
            if (row < 1 || row > snap.Rows || col < 1 || col > snap.Cols)
                throw new McpException($"Cursor position must be within 1-{snap.Rows}, 1-{snap.Cols}.");
            if (session.Edit(e => e.SetCursor(row - 1, col - 1)) == EditResult.Inhibited)
                throw new McpException("Keyboard is locked: the host is still processing. Call wait_for_screen first.");
        }

        if (!await session.SendAidAsync(aid, ct))
            throw new McpException("Keyboard is locked: the host is still processing. Call wait_for_screen first.");
        Audit("key", new { key = aid.ToString(), cursorRow, cursorCol });

        var ready = await session.WaitForInputReadyAsync(timeout, ct: ct);
        return Describe(ready ? $"Pressed {aid}." : $"Pressed {aid}; host still busy after {timeout.TotalSeconds:0}s, the screen may be partial. Call wait_for_screen.");
    }

    public static Aid ParseKey(string key)
    {
        var k = key.Trim();
        return k.ToLowerInvariant() switch
        {
            "enter" => Aid.Enter,
            "pageup" or "rolldown" => Aid.RollDown,
            "pagedown" or "rollup" => Aid.RollUp,
            "clear" => Aid.Clear,
            "help" => Aid.Help,
            "print" => Aid.Print,
            _ when k.Length is 2 or 3 && (k[0] is 'F' or 'f') && int.TryParse(k[1..], out var n) && n is >= 1 and <= 24
                => Enum.Parse<Aid>($"F{n}"),
            _ => throw new McpException($"Unknown key '{key}'. Use Enter, F1-F24, PageUp, PageDown, Clear, Help or Print."),
        };
    }

    // ---- Sign-on -----------------------------------------------------------

    public async Task<string> SignOnAsync(TimeSpan timeout, CancellationToken ct)
    {
        var session = Session;
        var host = _host!;
        if (_signOnFailed.Contains(host.Name))
            throw new McpException($"Sign-on to {host.Name} already failed in this server process. Retrying the same credentials would only count toward the profile's lockout; fix the configured credentials and restart the server.");
        if (settings.User is null)
            throw new McpException("QUAD5250_USER isn't set in the MCP server config.");
        var password = Environment.GetEnvironmentVariable(settings.PasswordEnvVar);
        if (string.IsNullOrEmpty(password))
            throw new McpException($"The password variable {settings.PasswordEnvVar} isn't set for the MCP server.");

        var (user, pass) = SignOnFields(session.GetSnapshot());
        if (user is null || pass is null)
            throw new McpException("This isn't a sign-on screen (no user and password fields).");

        var fill = session.Edit(e =>
        {
            var r = e.FillField(user, settings.User);
            return r == EditResult.Ok ? e.FillField(pass, password) : r;
        });
        if (fill != EditResult.Ok)
            throw new McpException($"Could not fill the sign-on fields: {fill}.");

        if (!await session.SendAidAsync(Aid.Enter, ct))
            throw new McpException("Keyboard is locked: the host is still processing. Call wait_for_screen first.");
        var ready = await session.WaitForInputReadyAsync(timeout, ct: ct);

        var after = session.GetSnapshot();
        var stillSignOn = SignOnFields(after) is ({ } u, { } p) && u.Row == user.Row && p.Row == pass.Row;
        if (stillSignOn)
        {
            _signOnFailed.Add(host.Name);
            Audit("sign_on", new { host = host.Name, user = settings.User, result = "failed" });
            throw new McpException(
                $"Sign-on to {host.Name} as {settings.User} was rejected. Host message: \"{after.RowText(after.Rows - 1).Trim()}\". " +
                "Further sign-on attempts are blocked in this server process to protect the profile from lockout.");
        }

        Audit("sign_on", new { host = host.Name, user = settings.User, result = "ok" });
        return Describe(ready ? $"Signed on to {host.Name} as {settings.User}." : $"Signed on as {settings.User}; host still busy.");
    }

    private static (FieldDefinition? User, FieldDefinition? Password) SignOnFields(ScreenSnapshot s)
        => (s.Fields.FirstOrDefault(f => f.IsEnterable && !f.IsNonDisplay),
            s.Fields.FirstOrDefault(f => f.IsEnterable && f.IsNonDisplay));

    // ---- Audit -------------------------------------------------------------

    /// <summary>
    /// Append one line per action. Field values are never written: screens
    /// can carry patient data and the log has no reason to.
    /// </summary>
    private void Audit(string action, object details)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settings.AuditLogPath)!);
            var line = JsonSerializer.Serialize(new { at = DateTimeOffset.Now, host = _host?.Name, action, details });
            File.AppendAllText(settings.AuditLogPath, line + Environment.NewLine);
        }
        catch (IOException) { /* auditing must not break the session */ }
        catch (UnauthorizedAccessException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null) await _session.DisposeAsync();
    }
}

public sealed record FieldValue(
    [property: System.ComponentModel.Description("Field id from read_screen (e.g. \"3\") or its exact label.")] string Field,
    [property: System.ComponentModel.Description("Text to put in the field. Replaces the whole field; empty clears it.")] string Value);
