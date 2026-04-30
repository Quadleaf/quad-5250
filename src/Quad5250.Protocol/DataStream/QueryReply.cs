using Quad5250.Protocol.Ebcdic;

namespace Quad5250.Protocol.DataStream;

/// <summary>
/// Builds a 5250 Query Reply structured field record describing the terminal's
/// capabilities. Sent in response to the host's WSF (0xF3) Query — IBM i hosts
/// upgrade the session to "5250 enhanced" only after this exchange, so without
/// it the session sits frozen on the screen that triggered the WSF.
/// </summary>
/// <remarks>
/// Byte-level layout cribbed directly from tn5250j's sendQueryResponse() — RFC
/// 1205 § 5.3 has the same content but tn5250j's exact field ordering and
/// reserved-byte values are what known-good IBM i hosts accept without a fuss.
///
/// Wire layout (74 bytes):
///   10-byte GDS header   : LL LL 12 A0 00 00 04 00 00 00
///   64-byte SF payload   : 00 00 88 00 3A D9 70 80 ... (see below)
/// </remarks>
public static class QueryReply
{
    public static byte[] Build(EbcdicTranslator ebcdic)
    {
        // 64-byte payload (matches tn5250j abyte0[]):
        var p = new byte[64];

        p[0] = 0x00;            // cursor row
        p[1] = 0x00;            // cursor col
        p[2] = 0x88;            // Inbound Write Structured Field AID
        p[3] = 0x00; p[4] = 0x3A; // SF length 58
        p[5] = 0xD9;            // class: 5250
        p[6] = 0x70;            // type: Query
        p[7] = 0x80;            // flags
        p[8] = 0x06; p[9] = 0x00;  // controller hardware class
        p[10] = 0x01; p[11] = 0x01; p[12] = 0x00; // controller code level 1.1.0
        // p[13..28] reserved zeros

        p[29] = 0x01;           // device type: 5250 Emulator
        // "5251011" in EBCDIC at p[30..36]
        var label = "5251011";
        Span<byte> tmp = stackalloc byte[label.Length];
        ebcdic.ToEbcdic(label, tmp);
        for (int k = 0; k < tmp.Length; k++) p[30 + k] = tmp[k];

        p[37] = 0x02;           // keyboard ID: standard
        // p[38..39] zeros
        // p[40..43] display serial number (tn5250j uses 00 24 24 00)
        p[40] = 0x00; p[41] = 0x24; p[42] = 0x24; p[43] = 0x00;
        p[44] = 0x01; p[45] = 0x00; // maximum input fields = 256
        // p[46..48] reserved zeros
        p[49] = 0x01;           // controller display capability
        p[50] = 0x11;           //   (17 — keep tn5250j's value verbatim)
        // p[51..53] zeros
        p[54] = 0x08;           // enhanced UI level 3 (window headers/footers)
        // p[55..63] reserved zeros

        // Wrap in 10-byte GDS header. tn5250j calls writeGDS(0, 0, ...) here —
        // flags=0, opcode=0 (the 0x88 marker inside the payload is what the
        // host keys off of, not the GDS opcode).
        var rec = new byte[10 + p.Length];
        var totalLen = rec.Length;
        rec[0] = (byte)(totalLen >> 8);
        rec[1] = (byte)totalLen;
        rec[2] = 0x12; rec[3] = 0xA0;
        rec[4] = 0x00; rec[5] = 0x00; // reserved
        rec[6] = 0x04;                // variable header length
        rec[7] = 0x00;                // flags
        rec[8] = 0x00;                // reserved
        rec[9] = 0x00;                // opcode
        Buffer.BlockCopy(p, 0, rec, 10, p.Length);
        return rec;
    }
}
