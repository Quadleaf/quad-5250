using System.Buffers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quad5250.Protocol.Telnet;

/// <summary>
/// TCP + telnet option negotiation for TN5250E (RFC 2877/4777).
/// Owns the socket and produces a stream of decoded 5250 records (telnet
/// IAC framing stripped, IAC IAC unescaped). Negotiation is handled
/// transparently by the read loop.
/// </summary>
public sealed class Tn5250Client : IAsyncDisposable
{
    private static readonly byte[] _supportedOptions =
    {
        TelnetCodes.OptBinary,
        TelnetCodes.OptEndOfRecord,
        TelnetCodes.OptTerminalType,
        TelnetCodes.OptNewEnviron,
    };

    private static int _deviceCounter;
    // Process-unique 3-char prefix so device names don't collide across restarts.
    private static readonly string _devicePrefix = $"Q{Random.Shared.Next(0, 0x10000):X4}";

    private readonly string _host;
    private readonly int _port;
    private readonly string _terminalType;
    private readonly string _deviceName;
    private readonly ILogger<Tn5250Client> _log;
    private readonly TelnetParser _parser = new();
    private readonly Channel<byte[]> _records =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    public Tn5250Client(string host, int port = 23, string terminalType = "IBM-3179-2", string? deviceName = null, ILogger<Tn5250Client>? log = null)
    {
        _host = host;
        _port = port;
        _terminalType = terminalType;
        // Device names are limited to 10 chars; auto-generate a unique-ish one
        // when not specified so multiple sessions don't collide.
        // 10-char max device name on AS/400. Randomized prefix per process +
        // monotonic counter avoids collisions across server restarts.
        _deviceName = deviceName ?? $"{_devicePrefix}{Interlocked.Increment(ref _deviceCounter):D5}";
        _log = log ?? NullLogger<Tn5250Client>.Instance;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_tcp is not null) throw new InvalidOperationException("Already connected.");

        _log.LogInformation("Connecting to {Host}:{Port}", _host, _port);
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_host, _port, ct);

        // Match tn5250j socket options: keepalive on, Nagle off, no linger.
        _tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _tcp.NoDelay = true;
        _tcp.LingerState = new LingerOption(false, 0);

        _stream = _tcp.GetStream();
        _log.LogInformation("TCP connected to {Host}:{Port}", _host, _port);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadRecordsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var record in _records.Reader.ReadAllAsync(ct))
        {
            yield return record;
        }
    }

    /// <summary>
    /// Send a 5250 record. The caller provides the raw datastream; we add
    /// IAC byte-stuffing and an IAC EOR terminator.
    /// </summary>
    public async Task SendRecordAsync(ReadOnlyMemory<byte> record, CancellationToken ct = default)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");

        var src = record.Span;
        _log.LogInformation("TX record {Length} bytes: {Hex}", src.Length, HexPreview(src));

        var rented = ArrayPool<byte>.Shared.Rent(record.Length * 2 + 2);
        try
        {
            int j = 0;
            for (int i = 0; i < src.Length; i++)
            {
                rented[j++] = src[i];
                if (src[i] == TelnetCodes.IAC) rented[j++] = TelnetCodes.IAC;
            }
            rented[j++] = TelnetCodes.IAC;
            rented[j++] = TelnetCodes.EOR;

            await _writeLock.WaitAsync(ct);
            try
            {
                await _stream.WriteAsync(rented.AsMemory(0, j), ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string HexPreview(ReadOnlySpan<byte> data)
    {
        const int max = 4096;
        var len = Math.Min(data.Length, max);
        var sb = new StringBuilder(len * 3);
        for (int i = 0; i < len; i++)
        {
            if (i > 0 && i % 32 == 0) sb.Append('\n');
            sb.Append(data[i].ToString("X2")).Append(' ');
        }
        if (data.Length > max) sb.Append("\n…").Append(data.Length - max).Append(" more");
        return sb.ToString().TrimEnd();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n;
                try
                {
                    n = await _stream!.ReadAsync(buffer, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }

                if (n == 0)
                {
                    _log.LogInformation("Stream closed by host (EOF)");
                    break;
                }

                _log.LogDebug("RX {Bytes} bytes: {Hex}", n, HexPreview(buffer, 0, n));

                foreach (var msg in _parser.Feed(buffer.AsSpan(0, n)))
                {
                    switch (msg)
                    {
                        case TelnetRecord r:
                            _log.LogInformation("RX record {Length} bytes: {Hex}", r.Data.Length, HexPreview(r.Data, 0, r.Data.Length));
                            await _records.Writer.WriteAsync(r.Data, ct);
                            break;
                        case TelnetNegotiate ne:
                            _log.LogInformation("RX negotiate {Cmd} {Opt}", CmdName(ne.Command), OptName(ne.Option));
                            await HandleNegotiateAsync(ne, ct);
                            break;
                        case TelnetSubneg sn:
                            _log.LogInformation("RX subneg {Opt} data={Hex}", OptName(sn.Option), HexPreview(sn.Data, 0, sn.Data.Length));
                            await HandleSubnegAsync(sn, ct);
                            break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            _records.Writer.TryComplete();
        }
    }

    private async Task HandleNegotiateAsync(TelnetNegotiate n, CancellationToken ct)
    {
        bool weSupport = Array.IndexOf(_supportedOptions, n.Option) >= 0;

        byte response;
        if (n.Command == TelnetCodes.DO)
            response = weSupport ? TelnetCodes.WILL : TelnetCodes.WONT;
        else if (n.Command == TelnetCodes.WILL)
            response = weSupport ? TelnetCodes.DO : TelnetCodes.DONT;
        else
            return; // ignore WONT/DONT

        _log.LogInformation("TX negotiate {Resp} {Opt}", CmdName(response), OptName(n.Option));
        await WriteRawAsync(new byte[] { TelnetCodes.IAC, response, n.Option }, ct);
    }

    private async Task HandleSubnegAsync(TelnetSubneg s, CancellationToken ct)
    {
        // Both TT and NEW-ENVIRON expect SEND (0x01) → respond with IS (0x00).
        if (s.Data.Length == 0 || s.Data[0] != TelnetCodes.EnvSend) return;

        if (s.Option == TelnetCodes.OptTerminalType)
        {
            var ttype = Encoding.ASCII.GetBytes(_terminalType);
            var resp = new byte[6 + ttype.Length];
            int i = 0;
            resp[i++] = TelnetCodes.IAC;
            resp[i++] = TelnetCodes.SB;
            resp[i++] = TelnetCodes.OptTerminalType;
            resp[i++] = TelnetCodes.EnvIs;
            ttype.CopyTo(resp, i); i += ttype.Length;
            resp[i++] = TelnetCodes.IAC;
            resp[i] = TelnetCodes.SE;
            _log.LogInformation("TX subneg TT IS {TermType}", _terminalType);
            await WriteRawAsync(resp, ct);
        }
        else if (s.Option == TelnetCodes.OptNewEnviron)
        {
            await SendNewEnvironIsAsync(ct);
        }
    }

    private async Task SendNewEnvironIsAsync(CancellationToken ct)
    {
        // RFC 4777 + tn5250j conventions. Only send the variables we actually
        // have values for: empty USER/IBMRSEED/IBMSUBSPW appear to upset some
        // hosts. Auto-signon vars (USER, IBMRSEED, IBMSUBSPW, IBMCURLIB,
        // IBMIMENU, IBMPROGRAM) are added later when we wire up credentials;
        // USER, when present, is tagged VAR rather than USERVAR.
        var buf = new List<byte>(96)
        {
            TelnetCodes.IAC, TelnetCodes.SB, TelnetCodes.OptNewEnviron, TelnetCodes.EnvIs,
        };

        WriteUserVar(buf, "KBDTYPE", "USB");
        WriteUserVar(buf, "CODEPAGE", "37");
        WriteUserVar(buf, "CHARSET", "697");
        WriteUserVar(buf, "DEVNAME", _deviceName);

        buf.Add(TelnetCodes.IAC);
        buf.Add(TelnetCodes.SE);

        _log.LogInformation("TX subneg NEW-ENVIRON IS DEVNAME={Dev}", _deviceName);
        await WriteRawAsync(buf.ToArray(), ct);
    }

    private static void WriteUserVar(List<byte> buf, string name, string value)
    {
        buf.Add(TelnetCodes.EnvUserVar);
        buf.AddRange(Encoding.ASCII.GetBytes(name));
        buf.Add(TelnetCodes.EnvValue);
        buf.AddRange(Encoding.ASCII.GetBytes(value));
    }

    private static string HexPreview(byte[] data, int offset, int length) =>
        HexPreview(data.AsSpan(offset, length));

    private static string CmdName(byte b) => b switch
    {
        TelnetCodes.WILL => "WILL",
        TelnetCodes.WONT => "WONT",
        TelnetCodes.DO => "DO",
        TelnetCodes.DONT => "DONT",
        _ => $"0x{b:X2}",
    };

    private static string OptName(byte b) => b switch
    {
        TelnetCodes.OptBinary => "BINARY",
        TelnetCodes.OptEcho => "ECHO",
        TelnetCodes.OptSuppressGoAhead => "SUPPRESS-GA",
        TelnetCodes.OptTerminalType => "TERMINAL-TYPE",
        TelnetCodes.OptEndOfRecord => "EOR",
        TelnetCodes.OptNewEnviron => "NEW-ENVIRON",
        _ => $"0x{b:X2}",
    };

    private async Task WriteRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream!.WriteAsync(bytes, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_readTask is not null)
        {
            try { await _readTask; } catch { /* shutdown */ }
        }
        _stream?.Dispose();
        _tcp?.Dispose();
        _cts?.Dispose();
        _writeLock.Dispose();
    }
}
