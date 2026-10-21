using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Quad5250.Desktop;

/// <summary>Per-user preferences in %APPDATA%\Quad5250\settings.json.</summary>
public sealed record AppSettings(string Host = "", int Port = 23, string Theme = "Green")
{
    internal static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quad5250");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}

public sealed record SavedCredentials(string Username, string Password);

/// <summary>
/// Sign-on credentials per host:port, encrypted with Windows DPAPI for the
/// current user (%APPDATA%\Quad5250\credentials.bin). Unlike the browser
/// client's localStorage, nothing is stored in plaintext.
/// </summary>
public static class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Quad5250.Desktop.Credentials");
    private static string FilePath => Path.Combine(AppSettings.Directory, "credentials.bin");

    private static string Key(string host, int port) => $"{host.ToLowerInvariant()}:{port}";

    public static SavedCredentials? Get(string host, int port)
        => ReadAll().TryGetValue(Key(host, port), out var creds) ? creds : null;

    public static void Save(string host, int port, SavedCredentials creds)
    {
        var all = ReadAll();
        all[Key(host, port)] = creds;
        WriteAll(all);
    }

    public static void Forget(string host, int port)
    {
        var all = ReadAll();
        if (all.Remove(Key(host, port))) WriteAll(all);
    }

    private static Dictionary<string, SavedCredentials> ReadAll()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var json = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, SavedCredentials>>(json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or UnauthorizedAccessException)
        {
            // Unreadable (another user's file, corrupted): treat as empty
            // rather than block sign-on. The next save overwrites it.
            return new();
        }
    }

    private static void WriteAll(Dictionary<string, SavedCredentials> all)
    {
        Directory.CreateDirectory(AppSettings.Directory);
        var json = JsonSerializer.SerializeToUtf8Bytes(all);
        File.WriteAllBytes(FilePath, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
    }
}
