using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Screen;
using Quad5250.Rendering;

namespace Quad5250.Desktop;

/// <summary>
/// Draws the 5250 screen and turns mouse clicks into cursor moves or
/// F-key presses. Keyboard handling lives in the window, which owns the
/// session. Sized to the grid at a fixed font size; the window scales it.
/// </summary>
public sealed class TerminalView : FrameworkElement
{
    private readonly TerminalRenderer _renderer = new(fontSize: 16);
    private ScreenSnapshot _snapshot = ScreenSnapshot.Blank();
    private TerminalTheme _theme = TerminalTheme.Green;
    private IReadOnlyList<FunctionKeyLabel> _fkeys = [];

    public event Action<int, int>? CellClicked;
    public event Action<Aid>? FunctionKeyClicked;

    public TerminalView()
    {
        Focusable = true;
        FocusVisualStyle = null;
    }

    public ScreenSnapshot Snapshot
    {
        get => _snapshot;
        set
        {
            var resized = value.Rows != _snapshot.Rows || value.Cols != _snapshot.Cols;
            _snapshot = value;
            _fkeys = FunctionKeyLabels.Find(value);
            if (resized) InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public TerminalTheme Theme
    {
        get => _theme;
        set { _theme = value; InvalidateVisual(); }
    }

    protected override Size MeasureOverride(Size availableSize) => _renderer.GridSize(_snapshot.Rows, _snapshot.Cols);

    protected override void OnRender(DrawingContext dc)
        => _renderer.Render(dc, _snapshot, _theme, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ChangedButton != MouseButton.Left) return;
        if (!_renderer.TryHitCell(e.GetPosition(this), _snapshot.Rows, _snapshot.Cols, out var row, out var col)) return;

        e.Handled = true;
        if (LabelAt(row, col) is { } label) FunctionKeyClicked?.Invoke(label.Aid);
        else CellClicked?.Invoke(row, col);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var label = _renderer.TryHitCell(e.GetPosition(this), _snapshot.Rows, _snapshot.Cols, out var row, out var col)
            ? LabelAt(row, col)
            : null;
        Cursor = label is not null ? Cursors.Hand : Cursors.IBeam;
        if (!Equals(ToolTip, label?.Text)) ToolTip = label?.Text;
    }

    private FunctionKeyLabel? LabelAt(int row, int col)
        => _fkeys.FirstOrDefault(l => l.Row == row && col >= l.StartCol && col < l.EndCol);
}
