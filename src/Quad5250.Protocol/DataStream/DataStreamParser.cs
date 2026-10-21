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
    private const byte ClassPrefix = 0x04;

    private readonly ScreenBuffer _screen;
    private readonly EbcdicTranslator _ebcdic;
    private readonly Queue<byte[]> _pendingResponses = new();
    private bool _keyboardUnlocked;
    private bool _readRequested;

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

    /// <summary>
    /// Apply one host record. The result says whether the host released the
    /// keyboard: a Write to Display whose second control character has the
    /// unlock bit, or a Read command (the host is waiting for an AID key).
    /// </summary>
    public ApplyResult Apply(ReadOnlySpan<byte> record)
    {
        _keyboardUnlocked = false;
        _readRequested = false;
        if (record.Length <= HeaderLength) return default;
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
                case Cmd.SaveScreen or Cmd.SavePartialScreen
                    when i > 0 && data[i - 1] == ClassPrefix:
                    // The host is about to open a window and waits for the
                    // screen image before going on. A partial save gets the
                    // whole screen: restoring all of it is a superset.
                    _pendingResponses.Enqueue(InboundRecord.Build(
                        [ClassPrefix, Cmd.RestoreScreen, .. _screen.ToDataStream(_ebcdic.ToEbcdic)]));
                    i++;
                    // Skip a partial save's area parameters up to the next command.
                    while (i < data.Length && data[i] != ClassPrefix) i++;
                    break;
                case Cmd.RestoreScreen or Cmd.RestorePartialScreen
                    when i > 0 && data[i - 1] == ClassPrefix:
                    // What follows is the stream we sent for Save Screen
                    // (Clear Unit, Write to Display, ...): it replays as-is.
                    i++;
                    break;
                case Cmd.ReadInputFields or Cmd.ReadMdtFields or Cmd.ReadMdtAlt
                    when i > 0 && data[i - 1] == ClassPrefix:
                    // Only after the 0x04 class prefix: these values can also
                    // turn up among the unknown bytes this loop skips.
                    _readRequested = true;
                    i++;
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
        _screen.ResolveAttributes();
        _screen.RebuildFields();
        return new ApplyResult(_keyboardUnlocked, _readRequested);
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
        // Two control characters follow the command. CC2 bit 0x08 unlocks the
        // keyboard (the IBM i we test against ends every input screen with CC2 = 0x18); the
        // rest (MDT reset, blink, alarm) isn't acted on yet.
        if (i + 2 > data.Length) return data.Length;
        if ((data[i + 1] & 0x08) != 0) _keyboardUnlocked = true;
        i += 2;

        while (i < data.Length)
        {
            var b = data[i];
            // 0x04 is the class prefix the host puts before every top-level
            // command (ClearUnit, WTD, ReadMDT, etc.). When we see it inside
            // WTD, the WTD payload is over — let the top-level loop pick up
            // the next command.
            if (b == ClassPrefix) break;

            if (b < 0x20)
            {
                if (IsOrder(b))
                    i = ApplyOrder(data, i);
                else
                {
                    // NULL (0x00) is a real cell — display as space and advance
                    // the buffer position. Skipping without advancing shifts
                    // every subsequent attr byte left, producing stray field
                    // markers in the wrong columns (e.g. WRKUSRJOB row 2).
                    _screen.Write(' ');
                    i++;
                }
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
                // Inherits the current sticky attribute set by the most recent
                // 0x20-0x3F byte on this WTD pass.
                _screen.Write(_ebcdic.ToUnicode(b));
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
        // buffer position up to and including the end address with the char,
        // inheriting the current sticky attribute. The IBM Sign On "logo" is
        // drawn this way — RA fills rectangular regions of spaces under
        // reverse-video attributes (0x21 = green block, 0x3B = blue block).
        if (i + 3 > data.Length) return data.Length;
        var endRow = data[i] - 1;
        var endCol = data[i + 1] - 1;
        // A NULL fill (Sign On repaints its Password field this way) displays
        // as space, matching how WTD treats NULL bytes; '\0' in the buffer
        // would otherwise read as content in fields and in screen text.
        var ch = data[i + 2] == 0x00 ? ' ' : _ebcdic.ToUnicode(data[i + 2]);

        var endIndex = endRow * _screen.Cols + endCol;
        var curIndex = _screen.BufferRow * _screen.Cols + _screen.BufferCol;
        var total = _screen.Rows * _screen.Cols;
        // Wrap if end is before start.
        var count = endIndex >= curIndex ? endIndex - curIndex + 1 : total - curIndex + endIndex + 1;
        for (int k = 0; k < count; k++) _screen.Write(ch);

        return i + 3;
    }

    private int ApplyEa(ReadOnlySpan<byte> data, int i)
    {
        // Erase To Address: end-row, end-col, type (length byte). Fill with
        // nulls (rendered as space) under the current sticky attribute.
        if (i + 3 > data.Length) return data.Length;
        var endRow = data[i] - 1;
        var endCol = data[i + 1] - 1;

        var endIndex = endRow * _screen.Cols + endCol;
        var curIndex = _screen.BufferRow * _screen.Cols + _screen.BufferCol;
        var total = _screen.Rows * _screen.Cols;
        var count = endIndex >= curIndex ? endIndex - curIndex + 1 : total - curIndex + endIndex + 1;
        for (int k = 0; k < count; k++) _screen.Write(' ');

        return i + 3;
    }

    private int ApplyStartOfField(ReadOnlySpan<byte> data, int i)
    {
        // SF format (5494 Functions Reference):
        //   1D [FFW1 FFW2] [FCW1 FCW2]* <AttrByte> <LenHi> <LenLo>
        // The FFW is present when its first byte's top bits are 01; without it
        // the field is output-only. FCW pairs (top bits 10) are optional and
        // carry features we don't act on, so they're skipped. The attribute
        // byte (top bits 00, 0x20-0x3F) is never ambiguous with either.
        // FFW info, the explicit field length, and a fromSf marker are all
        // recorded with the attribute mark so RebuildFields can build a
        // FieldDefinition without scanning. Length matters for fields where
        // NULLs (not attribute bytes) pad to the next SF — Sign On's User
        // and Password are 10 cells each but separated by 10 NULL bytes, so
        // the scan would otherwise compute the field as nearly a row long.
        byte ffw1 = 0, ffw2 = 0;
        if (i < data.Length && (data[i] & 0xC0) == 0x40)
        {
            if (i + 2 > data.Length) return data.Length;
            ffw1 = data[i];
            ffw2 = data[i + 1];
            i += 2;
            while (i < data.Length && (data[i] & 0xC0) == 0x80) i += 2;
        }

        if (i + 3 > data.Length) return data.Length;
        var attrByte = data[i];
        var length = (data[i + 1] << 8) | data[i + 2];

        _screen.WriteAttribute(attrByte, ffw1, ffw2, fromSf: true, length: length);

        return i + 3;
    }

    private static int SkipStartOfHeader(ReadOnlySpan<byte> data, int i)
    {
        // First byte after SOH is the header length; skip that many.
        if (i >= data.Length) return data.Length;
        var len = data[i];
        return Math.Min(i + 1 + len, data.Length);
    }
}

/// <summary>What a host record asked of the terminal, beyond drawing.</summary>
public readonly record struct ApplyResult(bool KeyboardUnlocked, bool ReadRequested)
{
    /// <summary>The operator may type and press an AID key again.</summary>
    public bool ReleasesKeyboard => KeyboardUnlocked || ReadRequested;
}
