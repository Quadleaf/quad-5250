using System.Text;

namespace Quad5250.Protocol.Ebcdic;

/// <summary>
/// EBCDIC ↔ Unicode translation backed by <see cref="Encoding.GetEncoding(int)"/>
/// from System.Text.Encoding.CodePages. Default CCSID is 037 (US English).
/// Common alternatives: 500 (International), 1140 (US+€), 1141 (DE), 1147 (FR), 285 (UK).
/// </summary>
/// <remarks>
/// 5250 attribute bytes occupy 0x20–0x3F in some datastream contexts and must NOT
/// be passed through this translator — that is the parser's responsibility.
/// </remarks>
public sealed class EbcdicTranslator
{
    private static readonly bool _providerRegistered = RegisterProvider();

    private static bool RegisterProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    }

    public int Ccsid { get; }

    private readonly Encoding _encoding;
    private readonly char[] _toUnicode = new char[256];

    public EbcdicTranslator(int ccsid = 37)
    {
        _ = _providerRegistered;
        Ccsid = ccsid;
        _encoding = Encoding.GetEncoding(ccsid);

        Span<byte> bytes = stackalloc byte[256];
        for (int i = 0; i < 256; i++) bytes[i] = (byte)i;
        _encoding.GetChars(bytes, _toUnicode);
    }

    public char ToUnicode(byte ebcdic) => _toUnicode[ebcdic];

    public byte ToEbcdic(char unicode)
    {
        Span<char> src = stackalloc char[1];
        Span<byte> dst = stackalloc byte[1];
        src[0] = unicode;
        _encoding.GetBytes(src, dst);
        return dst[0];
    }

    public void ToUnicode(ReadOnlySpan<byte> ebcdic, Span<char> unicode)
    {
        if (unicode.Length < ebcdic.Length)
            throw new ArgumentException("Destination too small", nameof(unicode));

        for (int i = 0; i < ebcdic.Length; i++)
            unicode[i] = _toUnicode[ebcdic[i]];
    }

    public int ToEbcdic(ReadOnlySpan<char> unicode, Span<byte> ebcdic)
        => _encoding.GetBytes(unicode, ebcdic);
}
