namespace Quad5250.Protocol.Session;

/// <summary>One input field's value, sent back to the host on an AID key.</summary>
public sealed record ModifiedField(int Row, int Col, string Text);
