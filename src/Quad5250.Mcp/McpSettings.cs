using System.IO;

namespace Quad5250.Mcp;

/// <summary>A host the server may connect to, by the name the model uses.</summary>
public sealed record HostEntry(string Name, string Host, int Port);

/// <summary>
/// Server configuration, read from environment variables set in the MCP
/// client config. Only listed hosts can be reached, so production is opt-in.
/// <list type="bullet">
///   <item>QUAD5250_HOSTS: "dev=10.0.0.5;test=host:2323" (port defaults to 23)</item>
///   <item>QUAD5250_USER: sign-on user profile</item>
///   <item>QUAD5250_PASSWORD_ENV: name of the variable holding the password (default QUAD5250_PASSWORD)</item>
///   <item>QUAD5250_SCREENSHOT_DIR: where screenshots go (default Pictures\Quad5250)</item>
///   <item>QUAD5250_AUDIT_LOG: action log path (default %APPDATA%\Quad5250\mcp-audit.log)</item>
/// </list>
/// </summary>
public sealed record McpSettings(
    IReadOnlyList<HostEntry> Hosts,
    string? User,
    string PasswordEnvVar,
    string ScreenshotDir,
    string AuditLogPath)
{
    public static McpSettings FromEnvironment()
    {
        // Not %LOCALAPPDATA%\Quad5250: that's the installer's app folder, which
        // Velopack replaces on reinstall and deletes on uninstall.
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quad5250");
        var pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Quad5250");
        return new McpSettings(
            ParseHosts(Environment.GetEnvironmentVariable("QUAD5250_HOSTS")),
            NullIfBlank(Environment.GetEnvironmentVariable("QUAD5250_USER")),
            NullIfBlank(Environment.GetEnvironmentVariable("QUAD5250_PASSWORD_ENV")) ?? "QUAD5250_PASSWORD",
            NullIfBlank(Environment.GetEnvironmentVariable("QUAD5250_SCREENSHOT_DIR")) ?? pictures,
            NullIfBlank(Environment.GetEnvironmentVariable("QUAD5250_AUDIT_LOG")) ?? Path.Combine(appData, "mcp-audit.log"));
    }

    public static IReadOnlyList<HostEntry> ParseHosts(string? value)
    {
        var hosts = new List<HostEntry>();
        if (string.IsNullOrWhiteSpace(value)) return hosts;

        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = entry.IndexOf('=');
            if (eq <= 0 || eq == entry.Length - 1)
                throw new FormatException($"QUAD5250_HOSTS entry '{entry}' must look like name=host or name=host:port.");
            var name = entry[..eq].Trim();
            var target = entry[(eq + 1)..].Trim();
            var port = 23;
            var colon = target.LastIndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(target[(colon + 1)..], out port) || port is < 1 or > 65535)
                    throw new FormatException($"QUAD5250_HOSTS entry '{entry}' has an invalid port.");
                target = target[..colon];
            }
            if (hosts.Any(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new FormatException($"QUAD5250_HOSTS lists '{name}' twice.");
            hosts.Add(new HostEntry(name, target, port));
        }
        return hosts;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
