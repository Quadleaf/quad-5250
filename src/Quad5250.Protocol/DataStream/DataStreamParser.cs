using Quad5250.Protocol.Ebcdic;
using Quad5250.Protocol.Screen;

namespace Quad5250.Protocol.DataStream;

/// <summary>
/// Parses a 5250 datastream record from the host and applies it to a screen
/// buffer. One instance per session; not thread-safe.
/// First-pass scope: ClearUnit / WriteToDisplay / SBA / IC / RA / plain text.
/// Start-of-Field, structured fields, and attribute decoding are TODO.
/// </summary>
/// <remarks>
/// Record framing (RFC 1205 / IBM SC30-3533):
///   bytes 0-1: total length, big-endian
///   bytes 2-3: GDS identifier 0x12 0xA0
///   byte 4   : reserved
///   byte 5-7 : outbound header / opcode
///   byte 8+  : 5250 commands (with possible additional sub-headers)
/// We strip the first 8 bytes and then walk forward, skipping any byte that
/// isn't a known command. Real records observed have additional class bytes
/// (e.g. 0x04 prefixes) before each command — skipping unknown bytes one at
/// a time is robust to those without needing to know the exact spec.
/// </remarks>
public sealed class DataStreamParser
{
    private const int HeaderLength = 8;

    private readonly ScreenBuffer _screen;
    private readonly EbcdicTranslator _ebcdic;
    private readonly Queue<byte[]> _pendingResponses = new();

    public DataStreamParser(ScreenBuffer screen, EbcdicTranslator ebcdic)
    {
        _screen = screen;
        _ebcdic = ebcdic;
    }

    /// <summary>
    /// Records that the parser needs to send back to the host (e.g. Query Reply
    /// in response to an inbound WSF Query). The session drains this queue
    /// after each Apply.
    /// </summary>
    public bool TryDequeueResponse(out byte[] record)
    {
        if (_pendingResponses.TryDequeue(out var r))
        {
            record = r;
            return true;
        }
        record = Array.Empty<byte>();
        return false;
    }

    public void Apply(ReadOnlySpan<byte> record)
    {
        if (record.Length <= HeaderLength) return;
        var data = record[HeaderLength..];

        int i = 0;
        while (i < data.Length)
        {
            var b = data[i];
            switch (b)
            {
                case Cmd.ClearUnit:
                case Cmd.ClearUnitAlternate:
                    _screen.Clear();
                    i++;
                    break;
                case Cmd.WriteToDisplay:
                case Cmd.WriteErrorCode:
                    i = ApplyWriteToDisplay(data, i + 1);
                    break;
                case Cmd.WriteStructuredField:
                    i = ApplyWriteStructuredField(data, i + 1);
                    break;
                default:
                    // Skip unknown wrapper bytes (e.g. inline class prefixes).
                    i++;
                    break;
            }
        }

        // Fields are derived from attribute byte positions on the screen, not
        // tracked incrementally — rebuild after every record so menus that
        // mix inline attribute bytes with SF orders both produce input fields.
        _screen.RebuildFields();
    }

    private int ApplyWriteStructuredField(ReadOnlySpan<byte> data, int i)
    {
        // SF: 2-byte length (BE) + class + type + flags/payload. The length
        // covers itself (so length=5 means class+type+1 trailing byte). The
        // host sends WSF Query (class D9, type 70) once per session — IBM i
        // upgrades to "5250 enhanced" mode based on our reply, and won't move
        // off the current screen until it has one.
        if (i + 2 > data.Length) return data.Length;
        var sfLen = (data[i] << 8) | data[i + 1];
        if (sfLen < 4 || i + sfLen > data.Length) return data.Length;
        var cls = data[i + 2];
        var type = data[i + 3];

        if (cls == 0xD9 && type == 0x70)
        {
            _pendingResponses.Enqueue(QueryReply.Build(_ebcdic));
        }

        return i + sfLen;
    }

    private int ApplyWriteToDisplay(ReadOnlySpan<byte> data, int i)
    {
        // 2 control character bytes follow the command; ignore for first pass.
        if (i + 2 > data.Length) return data.Length;
        i += 2;

        while (i < data.Length)
        {
            var b = data[i];
            // 0x04 is the class prefix the host puts before every top-level
            // command (ClearUnit, WTD, ReadMDT, etc.). When we see it inside
            // WTD, the WTD payload is over — let the top-level loop pick up
            // the next command.
            if (b == 0x04) break;

            if (b < 0x20)
            {
                if (IsOrder(b))
                    i = ApplyOrder(data, i);
                else
                    i++; // unknown low byte (NULL field-init bytes, etc.) — skip
            }
            else if (b < 0x40)
            {
                // Field attribute byte: occupies one cell, displays as space,
                // delimits a field. Recorded for later RebuildFields scan.
                _screen.WriteAttribute(b);
                i++;
            }
            else
            {
                _screen.Write(_ebcdic.ToUnicode(b), 0x20);
                i++;
            }
        }

        return i;
    }

    private static bool IsOrder(byte b) => b is
        Order.SetBufferAddress or
        Order.InsertCursor or
        Order.RepeatToAddress or
        Order.EraseToAddress or
        Order.MoveCursor or
        Order.StartOfHeader or
        Order.StartOfField;

    private int ApplyOrder(ReadOnlySpan<byte> data, int i)
    {
        var order = data[i++];
        return order switch
        {
            Order.SetBufferAddress => ApplySba(data, i),
            Order.InsertCursor => ApplyIc(data, i),
            Order.MoveCursor => ApplyIc(data, i),
            Order.RepeatToAddress => ApplyRa(data, i),
            Order.EraseToAddress => ApplyEa(data, i),
            Order.StartOfField => ApplyStartOfField(data, i),
            Order.StartOfHeader => SkipStartOfHeader(data, i),
            _ => data.Length,
        };
    }

    private int ApplySba(ReadOnlySpan<byte> data, int i)
    {
        if (i + 2 > data.Length) return data.Length;
        // 5250 addresses are 1-indexed.
        _screen.SetBufferAddress(data[i] - 1, data[i + 1] - 1);
        return i + 2;
    }

    private int ApplyIc(ReadOnlySpan<byte> data, int i)
    {
        if (i + 2 > data.Length) return data.Length;
        _screen.SetCursor(data[i] - 1, data[i + 1] - 1);
        return i + 2;
    }

    private int ApplyRa(ReadOnlySpan<byte> data, int i)
    {
        // Repeat To Address: end-row, end-col, char (EBCDIC). Fill from current
        // buffer position up to and including the end address with the char.
        if (i + 3 > data.Length) return data.Length;
        var endRow = data[i] - 1;
        var endCol = data[i + 1] - 1;
        var ch = _ebcdic.ToUnicode(data[i + 2]);

        var endIndex = endRow * _screen.Cols + endCol;
        var curIndex = _screen.BufferRow * _screen.Cols + _screen.BufferCol;
        var total = _screen.Rows * _screen.Cols;
        // Wrap if end is before start.
        var count = endIndex >= curIndex ? endIndex - curIndex + 1 : total - curIndex + endIndex + 1;
        for (int k = 0; k < count; k++) _screen.Write(ch, 0x20);

        return i + 3;
    }

    private int ApplyEa(ReadOnlySpan<byte> data, int i)
    {
        // Erase To Address: end-row, end-col, type (length byte). Fill with spaces.
        if (i + 3 > data.Length) return data.Length;
        var endRow = data[i] - 1;
        var endCol = data[i + 1] - 1;

        var endIndex = endRow * _screen.Cols + endCol;
        var curIndex = _screen.BufferRow * _screen.Cols + _screen.BufferCol;
        var total = _screen.Rows * _screen.Cols;
        var count = endIndex >= curIndex ? endIndex - curIndex + 1 : total - curIndex + endIndex + 1;
        for (int k = 0; k < count; k++) _screen.Write(' ', 0x20);

        return i + 3;
    }

    private int ApplyStartOfField(ReadOnlySpan<byte> data, int i)
    {
        // Observed SF format from real IBM i traffic:
        //   1D <FFW1> <FFW2> <AttrByte> <LenHi> <LenLo>
        // FFW info is recorded with the attribute mark so RebuildFields can
        // copy it onto the field; the explicit length is informational — the
        // scan derives length from the next attribute byte.
        if (i + 5 > data.Length) return data.Length;
        var ffw1 = data[i];
        var ffw2 = data[i + 1];
        var attrByte = data[i + 2];

        _screen.WriteAttribute(attrByte, ffw1, ffw2);

        return i + 5;
    }

    private static int SkipStartOfHeader(ReadOnlySpan<byte> data, int i)
    {
        // First byte after SOH is the header length; skip that many.
        if (i >= data.Length) return data.Length;
        var len = data[i];
        return Math.Min(i + 1 + len, data.Length);
    }
}
