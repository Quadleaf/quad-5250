using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Ebcdic;
using Quad5250.Protocol.Screen;
using Quad5250.Protocol.Telnet;

namespace Quad5250.Protocol.Session;

/// <summary>
/// Orchestrates a single 5250 session: owns the telnet client, the datastream
/// parser, the EBCDIC translator, and the screen buffer. Raises ScreenChanged
/// after applying each host record so the SignalR hub can push an update.
/// </summary>
public sealed class Tn5250Session : IAsyncDisposable
{
    private readonly Tn5250Client _client;
    private readonly DataStreamParser _parser;
    private readonly ILogger<Tn5250Session> _log;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private volatile bool _disconnected;

    public ScreenBuffer Screen { get; }
    public EbcdicTranslator Ebcdic { get; }
    public bool IsConnected => !_disconnected;

    public event Action? ScreenChanged;
    public event Action? Disconnected;

    public Tn5250Session(string host, int port = 23, int ccsid = 37, ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        _log = loggerFactory.CreateLogger<Tn5250Session>();
        _client = new Tn5250Client(host, port, log: loggerFactory.CreateLogger<Tn5250Client>());
        Ebcdic = new EbcdicTranslator(ccsid);
        Screen = new ScreenBuffer();
        _parser = new DataStreamParser(Screen, Ebcdic);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_runTask is not null) throw new InvalidOperationException("Already started.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _client.ConnectAsync(_cts.Token);
        _runTask = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var record in _client.ReadRecordsAsync(ct))
            {
                _log.LogInformation("Applying record ({Length} bytes)", record.Length);
                _parser.Apply(record.Span);
                ScreenChanged?.Invoke();

                // Parser may have queued protocol-level responses (e.g. Query
                // Reply when the host sent a WSF Query). Drain and send.
                while (_parser.TryDequeueResponse(out var resp))
                {
                    try
                    {
                        await _client.SendRecordAsync(resp, ct);
                        _log.LogInformation("Sent parser response ({Length} bytes)", resp.Length);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Parser response send failed");
                    }
                }
            }
            _log.LogInformation("Read loop ended (host closed connection)");
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "Read loop crashed");
        }
        finally
        {
            _disconnected = true;
            Disconnected?.Invoke();
        }
    }

    /// <summary>
    /// Send a Read MDT Fields response: GDS header + cursor row/col (1-indexed)
    /// + AID byte, followed by SBA + EBCDIC text for each modified field the
    /// caller passes in. The cursor position is the user's reported position
    /// (where they had focus when they pressed the AID key).
    /// </summary>
    public Task SendKeyAsync(
        Aid aid,
        IReadOnlyList<ModifiedField>? modifiedFields = null,
        int cursorRow = 0,
        int cursorCol = 0,
        CancellationToken ct = default)
    {
        if (_disconnected)
            throw new InvalidOperationException("Host has closed the connection. Reconnect to continue.");

        modifiedFields ??= Array.Empty<ModifiedField>();

        // 10-byte GDS header + 3-byte payload (cursor + AID) + per modified field (3 SBA + text)
        var capacity = 13;
        foreach (var f in modifiedFields) capacity += 3 + f.Text.Length;
        var buf = new byte[capacity];

        int p = 0;
        // GDS header per RFC 1205 / tn5250j writeGDS: 10 bytes total.
        // LL LL 12 A0 00 00 04 00 00 03 — that 0x04 is the "variable header
        // length" and the host requires it; 0x03 is the response opcode used
        // for AID (Put/Get) records.
        p += 2;            // length placeholder
        buf[p++] = 0x12; buf[p++] = 0xA0;
        buf[p++] = 0x00; buf[p++] = 0x00;   // reserved (16 bits)
        buf[p++] = 0x04;                    // variable header length
        buf[p++] = 0x00;                    // flags
        buf[p++] = 0x00;                    // reserved
        buf[p++] = 0x03;                    // opcode: Put/Get (AID response)

        // Read response payload: cursor row/col (1-indexed), AID byte
        buf[p++] = (byte)(cursorRow + 1);
        buf[p++] = (byte)(cursorCol + 1);
        buf[p++] = (byte)aid;

        // Modified fields: each is SBA + EBCDIC text
        Span<byte> ebcdic = stackalloc byte[80];
        foreach (var f in modifiedFields)
        {
            buf[p++] = DataStream.Order.SetBufferAddress;
            buf[p++] = (byte)(f.Row + 1);
            buf[p++] = (byte)(f.Col + 1);

            var len = Math.Min(f.Text.Length, ebcdic.Length);
            var written = Ebcdic.ToEbcdic(f.Text.AsSpan(0, len), ebcdic[..len]);
            for (int k = 0; k < written; k++) buf[p++] = ebcdic[k];
        }

        // Fill total length (big-endian) into header
        buf[0] = (byte)(p >> 8);
        buf[1] = (byte)p;

        return _client.SendRecordAsync(buf.AsMemory(0, p), ct);
    }

    public Task SendTextAsync(string text, CancellationToken ct = default)
    {
        // Typing is buffered client-side and shipped with the AID-key read response.
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_runTask is not null)
        {
            try { await _runTask; } catch { /* shutdown */ }
        }
        await _client.DisposeAsync();
        _cts?.Dispose();
    }
}
