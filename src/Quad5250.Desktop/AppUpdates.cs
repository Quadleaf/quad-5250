using System.Reflection;
using Velopack;

namespace Quad5250.Desktop;

/// <summary>
/// Background updates through Velopack. The feed URL is baked in at build
/// time (UpdateFeedUrl) and can be overridden with QUAD5250_UPDATE_URL;
/// with neither, or when running from a dev build rather than an install,
/// nothing is checked.
/// </summary>
public sealed class AppUpdates
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _ready;

    public AppUpdates()
    {
        var url = Environment.GetEnvironmentVariable("QUAD5250_UPDATE_URL") is { Length: > 0 } env
            ? env
            : Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "UpdateFeedUrl")?.Value;
        if (string.IsNullOrWhiteSpace(url)) return;

        var manager = new UpdateManager(url);
        if (manager.IsInstalled) _manager = manager;
    }

    /// <summary>Installed version, or null for a dev build.</summary>
    public string? CurrentVersion => _manager?.CurrentVersion?.ToString();

    /// <summary>
    /// Check and download in the background. Returns the version that's
    /// ready to apply, or null if there's none (or the feed is unreachable:
    /// being offline or off VPN must never get in the user's way).
    /// </summary>
    public async Task<string?> DownloadAsync()
    {
        if (_manager is null) return null;
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) return null;
            await _manager.DownloadUpdatesAsync(info);
            _ready = info;
            return info.TargetFullRelease.Version.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Close the app, apply the downloaded update and start the new version.</summary>
    public void ApplyAndRestart()
    {
        if (_manager is not null && _ready is not null) _manager.ApplyUpdatesAndRestart(_ready);
    }
}
