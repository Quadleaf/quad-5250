using Quad5250.Protocol.Ebcdic;
using Quad5250.Protocol.Session;

namespace Quad5250.Protocol.DataStream;

/// <summary>Builds the inbound record sent to the host when the operator presses an AID key.</summary>
public static class AidResponse
{
    /// <summary>
    /// AID keys that return only cursor + AID, never field data
    /// (5494 Functions Reference, Read MDT Fields response).
    /// </summary>
    public static bool SendsFieldData(Aid aid)
        => aid is not (Aid.Clear or Aid.Help or Aid.Print or Aid.RecordBackspace);

    /// <summary>
    /// Read MDT Fields response: GDS header + cursor row/col (1-indexed)
    /// + AID byte, followed by SBA + EBCDIC text for each modified field.
    /// The cursor position is where the operator had focus when they pressed
    /// the AID key.
    /// </summary>
    public static byte[] Build(
        EbcdicTranslator ebcdic,
        Aid aid,
        IReadOnlyList<ModifiedField> modifiedFields,
        int cursorRow,
        int cursorCol)
    {
        var encoded = SendsFieldData(aid)
            ? modifiedFields.Select(f => (Field: f, Bytes: ebcdic.ToEbcdic(f.Text))).ToList()
            : [];

        // 10-byte GDS header + 3-byte payload (cursor + AID) + per modified field (3 SBA + text)
        var length = 13 + encoded.Sum(e => 3 + e.Bytes.Length);
        if (length > ushort.MaxValue)
            throw new InvalidOperationException($"AID response of {length} bytes exceeds the 5250 record limit.");
        var buf = new byte[length];

        InboundRecord.WriteHeader(buf, length);
        int p = InboundRecord.HeaderLength;

        buf[p++] = (byte)(cursorRow + 1);
        buf[p++] = (byte)(cursorCol + 1);
        buf[p++] = (byte)aid;

        foreach (var (field, bytes) in encoded)
        {
            buf[p++] = Order.SetBufferAddress;
            buf[p++] = (byte)(field.Row + 1);
            buf[p++] = (byte)(field.Col + 1);
            bytes.CopyTo(buf, p);
            p += bytes.Length;
        }

        return buf;
    }
}

/// <summary>Framing for records the terminal sends to the host.</summary>
public static class InboundRecord
{
    public const int HeaderLength = 10;

    /// <summary>
    /// GDS header per RFC 1205 / tn5250j writeGDS: 10 bytes total.
    /// LL LL 12 A0 00 00 04 00 00 03 — that 0x04 is the "variable header
    /// length" and the host requires it; 0x03 is the Put/Get opcode used for
    /// AID responses and Save Screen replies.
    /// </summary>
    public static void WriteHeader(Span<byte> buf, int length)
    {
        buf[0] = (byte)(length >> 8);
        buf[1] = (byte)length;
        buf[2] = 0x12; buf[3] = 0xA0;
        buf[4] = 0x00; buf[5] = 0x00;   // reserved (16 bits)
        buf[6] = 0x04;                  // variable header length
        buf[7] = 0x00;                  // flags
        buf[8] = 0x00;                  // reserved
        buf[9] = 0x03;                  // opcode: Put/Get
    }

    /// <summary>Header plus payload.</summary>
    public static byte[] Build(ReadOnlySpan<byte> payload)
    {
        var length = HeaderLength + payload.Length;
        if (length > ushort.MaxValue)
            throw new InvalidOperationException($"Record of {length} bytes exceeds the 5250 record limit.");
        var buf = new byte[length];
        WriteHeader(buf, length);
        payload.CopyTo(buf.AsSpan(HeaderLength));
        return buf;
    }
}
