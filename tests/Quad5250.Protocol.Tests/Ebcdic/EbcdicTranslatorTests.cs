using Quad5250.Protocol.Ebcdic;
using Xunit;

namespace Quad5250.Protocol.Tests.Ebcdic;

public class EbcdicTranslatorTests
{
    [Theory]
    [InlineData(0xC1, 'A')]
    [InlineData(0xC2, 'B')]
    [InlineData(0xC9, 'I')]
    [InlineData(0xD1, 'J')]
    [InlineData(0xD9, 'R')]
    [InlineData(0xE2, 'S')]
    [InlineData(0xE9, 'Z')]
    [InlineData(0x81, 'a')]
    [InlineData(0x89, 'i')]
    [InlineData(0x91, 'j')]
    [InlineData(0x99, 'r')]
    [InlineData(0xA2, 's')]
    [InlineData(0xA9, 'z')]
    [InlineData(0xF0, '0')]
    [InlineData(0xF9, '9')]
    [InlineData(0x40, ' ')]
    [InlineData(0x5B, '$')]
    [InlineData(0x6B, ',')]
    [InlineData(0x4B, '.')]
    [InlineData(0x7C, '@')]
    public void Cp037_ToUnicode_KnownMappings(byte ebcdic, char expected)
    {
        var t = new EbcdicTranslator(37);
        Assert.Equal(expected, t.ToUnicode(ebcdic));
    }

    [Theory]
    [InlineData('A', 0xC1)]
    [InlineData('Z', 0xE9)]
    [InlineData('a', 0x81)]
    [InlineData('z', 0xA9)]
    [InlineData('0', 0xF0)]
    [InlineData('9', 0xF9)]
    [InlineData(' ', 0x40)]
    [InlineData('$', 0x5B)]
    public void Cp037_ToEbcdic_KnownMappings(char unicode, byte expected)
    {
        var t = new EbcdicTranslator(37);
        Assert.Equal(expected, t.ToEbcdic(unicode));
    }

    [Fact]
    public void RoundTrip_PrintableAscii_Cp037()
    {
        var t = new EbcdicTranslator(37);
        const string input = "Hello, World! 0123456789 $@#";

        Span<byte> bytes = stackalloc byte[input.Length];
        Span<char> chars = stackalloc char[input.Length];

        var written = t.ToEbcdic(input, bytes);
        Assert.Equal(input.Length, written);

        t.ToUnicode(bytes, chars);
        Assert.Equal(input, new string(chars));
    }

    [Fact]
    public void BulkAndScalar_Agree()
    {
        var t = new EbcdicTranslator(37);
        Span<byte> bytes = stackalloc byte[256];
        Span<char> bulk = stackalloc char[256];
        for (int i = 0; i < 256; i++) bytes[i] = (byte)i;

        t.ToUnicode(bytes, bulk);

        for (int i = 0; i < 256; i++)
            Assert.Equal(t.ToUnicode((byte)i), bulk[i]);
    }

    [Fact]
    public void Cp1141_DiffersFromCp037_OnGermanChars()
    {
        // Code points 0x4A, 0x5A, 0x7C are the EBCDIC slots that differ
        // between CP037 and CP1141 (German) — they hold Ä/Ö/§ in CP1141
        // versus ¢/!/@ in CP037.
        var us = new EbcdicTranslator(37);
        var de = new EbcdicTranslator(1141);

        Assert.NotEqual(us.ToUnicode(0x4A), de.ToUnicode(0x4A));
        Assert.NotEqual(us.ToUnicode(0x5A), de.ToUnicode(0x5A));
    }

    [Fact]
    public void DefaultCcsid_Is37()
    {
        var t = new EbcdicTranslator();
        Assert.Equal(37, t.Ccsid);
    }
}
