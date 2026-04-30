namespace Quad5250.Protocol.Telnet;

/// <summary>
/// Pure state machine that consumes raw bytes from a telnet stream and emits
/// decoded <see cref="TelnetMessage"/> values. No I/O, single-threaded.
/// State persists across calls so input may be split arbitrarily.
/// Refs: RFC 854 (telnet), RFC 1205 (TN5250), RFC 2877/4777 (TN5250E).
/// </summary>
public sealed class TelnetParser
{
    private enum State { Data, Iac, Cmd, Sub, SubIac }

    private State _state = State.Data;
    private byte _cmd;
    private readonly List<byte> _record = new(256);
    private readonly List<byte> _sub = new(64);

    /// <summary>Feed one chunk of bytes; returns the messages it produced.</summary>
    public List<TelnetMessage> Feed(ReadOnlySpan<byte> input)
    {
        var output = new List<TelnetMessage>();
        for (int i = 0; i < input.Length; i++)
        {
            Step(input[i], output);
        }
        return output;
    }

    private void Step(byte b, List<TelnetMessage> output)
    {
        switch (_state)
        {
            case State.Data:
                if (b == TelnetCodes.IAC) _state = State.Iac;
                else _record.Add(b);
                break;

            case State.Iac:
                if (b == TelnetCodes.IAC)
                {
                    _record.Add(TelnetCodes.IAC);
                    _state = State.Data;
                }
                else if (b == TelnetCodes.EOR)
                {
                    output.Add(new TelnetRecord(_record.ToArray()));
                    _record.Clear();
                    _state = State.Data;
                }
                else if (b == TelnetCodes.SB)
                {
                    _sub.Clear();
                    _state = State.Sub;
                }
                else if (b is TelnetCodes.WILL or TelnetCodes.WONT
                          or TelnetCodes.DO or TelnetCodes.DONT)
                {
                    _cmd = b;
                    _state = State.Cmd;
                }
                else
                {
                    // Unknown 2-byte command (NOP, AYT, BRK, etc.) — swallow and resume.
                    _state = State.Data;
                }
                break;

            case State.Cmd:
                output.Add(new TelnetNegotiate(_cmd, b));
                _state = State.Data;
                break;

            case State.Sub:
                if (b == TelnetCodes.IAC) _state = State.SubIac;
                else _sub.Add(b);
                break;

            case State.SubIac:
                if (b == TelnetCodes.SE)
                {
                    if (_sub.Count > 0)
                    {
                        var option = _sub[0];
                        var data = new byte[_sub.Count - 1];
                        for (int k = 1; k < _sub.Count; k++) data[k - 1] = _sub[k];
                        output.Add(new TelnetSubneg(option, data));
                    }
                    _sub.Clear();
                    _state = State.Data;
                }
                else if (b == TelnetCodes.IAC)
                {
                    _sub.Add(TelnetCodes.IAC);
                    _state = State.Sub;
                }
                else
                {
                    // Unexpected byte inside subneg; abort it.
                    _sub.Clear();
                    _state = State.Data;
                }
                break;
        }
    }
}
