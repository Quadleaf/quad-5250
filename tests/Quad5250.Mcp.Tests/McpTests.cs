using ModelContextProtocol;
using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Input;
using Quad5250.Protocol.Screen;
using Quad5250.Rendering;
using Xunit;

namespace Quad5250.Mcp.Tests;

public class McpSettingsTests
{
    [Fact]
    public void ParseHosts_NamesHostsAndPorts()
    {
        var hosts = McpSettings.ParseHosts("dev=10.0.0.5; test = box.local:2323");

        Assert.Equal(new HostEntry("dev", "10.0.0.5", 23), hosts[0]);
        Assert.Equal(new HostEntry("test", "box.local", 2323), hosts[1]);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("dev=")]
    [InlineData("dev=host:99999")]
    [InlineData("dev=a;DEV=b")]
    public void ParseHosts_RejectsMalformedEntries(string value)
        => Assert.Throws<FormatException>(() => McpSettings.ParseHosts(value));

    [Fact]
    public void ParseHosts_EmptyMeansNoHosts() => Assert.Empty(McpSettings.ParseHosts(null));
}

public class KeyTests
{
    [Theory]
    [InlineData("Enter", Aid.Enter)]
    [InlineData("f3", Aid.F3)]
    [InlineData("F24", Aid.F24)]
    [InlineData("PageDown", Aid.RollUp)]
    [InlineData("pageup", Aid.RollDown)]
    [InlineData("Clear", Aid.Clear)]
    public void ParseKey_MapsNames(string key, Aid expected) => Assert.Equal(expected, TerminalService.ParseKey(key));

    [Theory]
    [InlineData("F25")]
    [InlineData("F0")]
    [InlineData("Escape")]
    public void ParseKey_RejectsUnknown(string key) => Assert.Throws<McpException>(() => TerminalService.ParseKey(key));
}

public class ScreenDescriptionTests
{
    private static ScreenSnapshot SignOnLike()
    {
        var s = new ScreenBuffer();
        void Text(int row, int col, string text) { s.SetBufferAddress(row, col); foreach (var c in text) s.Write(c, 0x20); }
        void Field(int row, int col, int len, byte attr = 0x24)
        {
            s.SetBufferAddress(row, col - 1);
            s.WriteAttribute(attr, 0x40, 0x00, fromSf: true, length: len);
        }

        Text(5, 16, "User  . . . . . . . . . . . . . .");
        Field(5, 52, 10);
        Text(6, 16, "Password  . . . . . . . . . . . .");
        Field(6, 52, 10, attr: 0x27);
        Text(19, 1, "===>");
        Field(19, 6, 20);
        s.RebuildFields();
        new FieldEditor(s).FillField(s.Fields[1], "secret");
        return s.TakeSnapshot();
    }

    [Fact]
    public void Fields_GetIdsAndLabels()
    {
        var fields = ScreenDescription.Fields(SignOnLike());

        Assert.Equal(["User", "Password", "===>"], fields.Select(f => f.Label));
        Assert.Equal([1, 2, 3], fields.Select(f => f.Id));
    }

    [Fact]
    public void Label_InsideHostDrawnWindow_DropsTheBorder()
    {
        // Layout of the payment batch filter window as the host draws it.
        var s = new ScreenBuffer();
        s.SetBufferAddress(10, 14);
        foreach (var c in ":  Select Confirmed/Unconfirmed/All.  ") s.Write(c, 0x20);
        s.SetBufferAddress(10, 51);
        s.WriteAttribute(0x24, 0x40, 0x20, fromSf: true, length: 1);
        s.SetBufferAddress(12, 14);
        foreach (var c in ":  Select Date Range From/To........  ") s.Write(c, 0x20);
        s.SetBufferAddress(12, 51);
        s.WriteAttribute(0x24, 0x43, 0x00, fromSf: true, length: 10);
        s.RebuildFields();

        var labels = ScreenDescription.Fields(s.TakeSnapshot()).Select(f => f.Label);

        Assert.Equal(["Select Confirmed/Unconfirmed/All", "Select Date Range From/To"], labels);
    }

    [Fact]
    public void Describe_NeverShowsHiddenFieldContent()
    {
        var text = ScreenDescription.Describe(SignOnLike());

        Assert.DoesNotContain("secret", text);
        Assert.Contains("[2] \"Password\" row 7 col 53 len 10 = (hidden, 6 chars)", text);
        Assert.Contains(" 6| ", text);
    }

    [Fact]
    public void Svg_RedactsAndMasks()
    {
        var snap = SignOnLike();
        var svg = TerminalSvg.Render(snap, new ScreenshotOptions { Redactions = [new CellRange(5, 16, 4)] });

        Assert.StartsWith("<svg", svg);
        Assert.DoesNotContain("secret", svg);
        Assert.DoesNotContain(">User", svg);
        Assert.Contains("fill-opacity", svg);
    }
}
