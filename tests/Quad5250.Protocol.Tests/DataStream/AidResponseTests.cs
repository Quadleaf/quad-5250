using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Ebcdic;
using Quad5250.Protocol.Session;
using Xunit;

namespace Quad5250.Protocol.Tests.DataStream;

public class AidResponseTests
{
    private static readonly EbcdicTranslator Ebcdic = new();

    [Fact]
    public void Build_HeaderCursorAidAndField()
    {
        var record = AidResponse.Build(Ebcdic, Aid.Enter, [new ModifiedField(5, 20, "AB")], 5, 22);

        Assert.Equal(new byte[]
        {
            0x00, 0x12, 0x12, 0xA0, 0x00, 0x00, 0x04, 0x00, 0x00, 0x03,
            0x06, 0x17, 0xF1,
            Order.SetBufferAddress, 0x06, 0x15, 0xC1, 0xC2,
        }, record);
    }

    [Fact]
    public void Build_FieldLongerThan80_IsNotTruncated()
    {
        var text = new string('X', 200);

        var record = AidResponse.Build(Ebcdic, Aid.Enter, [new ModifiedField(0, 0, text)], 0, 0);

        Assert.Equal(13 + 3 + 200, record.Length);
        Assert.Equal(record.Length, (record[0] << 8) | record[1]);
        Assert.All(record[16..], b => Assert.Equal(0xE7, b));
    }

    [Theory]
    [InlineData(Aid.Clear)]
    [InlineData(Aid.Help)]
    [InlineData(Aid.Print)]
    public void Build_KeysThatSendNoData_OmitFields(Aid aid)
    {
        var record = AidResponse.Build(Ebcdic, aid, [new ModifiedField(5, 20, "AB")], 0, 0);

        Assert.Equal(13, record.Length);
        Assert.Equal((byte)aid, record[12]);
    }
}
