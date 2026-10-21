using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Ebcdic;
using Quad5250.Protocol.Input;
using Quad5250.Protocol.Screen;
using Quad5250.Protocol.Telnet;

namespace Quad5250.Protocol.Session;

/// <summary>
/// Orchestrates a single 5250 session: owns the telnet client, the datastream
/// parser, the EBCDIC translator, and the screen buffer. Raises ScreenChanged
/// after applying each host record so the SignalR hub can push an update.
/// In-process clients (desktop, headless) edit fields through <see cref="Edit"/>
/// and press AID keys with <see cref="SendAidAsync"/>; the browser path keeps its
/// own typed buffer and calls <see cref="SendKeyAsync"/>.
/// </summary>
public sealed class Tn5250Session : IAsyncDisposable
{
    private readonly Tn5250Client _client;
    private readonly DataStreamParser _parser;
    private readonly ILogger<Tn5250Session> _log;

    private readonly FieldEditor _editor;
    // Guards the screen buffer: the read loop applies host records while the
    // UI thread edits fields and takes snapshots.
    private readonly object _sync = new();
    private bool _keyboardLocked;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private volatile bool _disconnected;

    public ScreenBuffer Screen { get; }
    public EbcdicTranslator Ebcdic { get; }
    public bool IsConnected => !_disconnected;

    /// <summary>
    /// True from an AID key until the host's next record. A real 5250 waits
    /// for the WTD control character's keyboard-unlock bit; we unlock on any
    /// record, which is what the browser client has always done.
    /// </summary>
    public bool IsKeyboardLocked { get { lock (_sync) return _keyboardLocked; } }

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
        _editor = new FieldEditor(Screen);
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
                lock (_sync)
                {
                    _parser.Apply(record.Span);
                    _editor.OnHostWrite();
                    _keyboardLocked = false;
                }
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
    /// Send a Read MDT Fields response with modified fields the caller
    /// tracked itself (the browser keeps its own typed-character buffer).
    /// In-process clients should use <see cref="SendAidAsync"/>, which reads
    /// the modified fields from the screen buffer.
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

        var record = AidResponse.Build(Ebcdic, aid, modifiedFields ?? Array.Empty<ModifiedField>(), cursorRow, cursorCol);
        return _client.SendRecordAsync(record, ct);
    }

    /// <summary>
    /// Press an AID key: send the fields edited through <see cref="Edit"/>
    /// plus the cursor position, and lock the keyboard until the host's next
    /// record. Returns false without sending when the keyboard is already
    /// locked (a previous AID is still outstanding).
    /// </summary>
    public async Task<bool> SendAidAsync(Aid aid, CancellationToken ct = default)
    {
        byte[] record;
        lock (_sync)
        {
            if (_disconnected)
                throw new InvalidOperationException("Host has closed the connection. Reconnect to continue.");
            if (_keyboardLocked) return false;

            record = AidResponse.Build(Ebcdic, aid, _editor.GetModifiedFields(), Screen.CursorRow, Screen.CursorCol);
            _keyboardLocked = true;
        }

        try
        {
            await _client.SendRecordAsync(record, ct);
            return true;
        }
        catch
        {
            lock (_sync) _keyboardLocked = false;
            throw;
        }
    }

    /// <summary>
    /// Run an operator edit against the screen buffer, serialized with host
    /// writes. Returns <see cref="EditResult.Inhibited"/> without running it
    /// while the keyboard is locked or the host has disconnected.
    /// </summary>
    public EditResult Edit(Func<FieldEditor, EditResult> edit)
    {
        lock (_sync)
        {
            if (_keyboardLocked || _disconnected) return EditResult.Inhibited;
            return edit(_editor);
        }
    }

    /// <summary>Cursor-only edits (tab, arrows) that always succeed when the keyboard is free.</summary>
    public EditResult Edit(Action<FieldEditor> edit)
        => Edit(e => { edit(e); return EditResult.Ok; });

    /// <summary>Read the screen under the session lock, e.g. a field's current content.</summary>
    public T Read<T>(Func<FieldEditor, ScreenBuffer, T> read)
    {
        lock (_sync) return read(_editor, Screen);
    }

    public ScreenSnapshot GetSnapshot()
    {
        lock (_sync) return Screen.TakeSnapshot(_keyboardLocked);
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
