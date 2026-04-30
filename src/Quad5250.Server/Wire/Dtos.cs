namespace Quad5250.Server.Wire;

/// <summary>
/// Snapshot of the screen pushed to the browser. Cells is row-major,
/// length = Rows * Cols. Each cell is the rendered char plus its 5250
/// attribute byte (color/intensity/etc.) — the React side decodes this
/// into CSS classes.
/// </summary>
public sealed record ScreenUpdate(
    int Rows,
    int Cols,
    int CursorRow,
    int CursorCol,
    WireCell[] Cells,
    WireField[] Fields);

public readonly record struct WireCell(string Ch, byte Attr);

public sealed record WireField(
    int Row,
    int Col,
    int Length,
    bool IsInput,
    bool IsNonDisplay,
    bool IsBypass,
    bool IsNumeric);

/// <summary>
/// Connection request from the browser. Host/port are configurable per
/// connection so a single backend can broker sessions to multiple IBM i hosts.
/// </summary>
public sealed record ConnectRequest(string Host, int Port = 23, int Ccsid = 37);

/// <summary>
/// Keystroke from the browser. For AID keys (Enter / F-keys / Clear / etc.)
/// the client also ships the modified input fields it has buffered locally,
/// plus the current cursor position. Server uses these to build a 5250 Read
/// MDT Fields response.
/// </summary>
public sealed record KeyInput(
    string? Text,
    string? AidKey,
    ModifiedFieldDto[]? ModifiedFields = null,
    int CursorRow = 0,
    int CursorCol = 0);

public sealed record ModifiedFieldDto(int Row, int Col, string Text);
