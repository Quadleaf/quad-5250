using Microsoft.AspNetCore.SignalR;
using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Session;
using Quad5250.Server.Sessions;
using Quad5250.Server.Wire;

namespace Quad5250.Server.Hubs;

public sealed class TerminalHub : Hub
{
    private readonly SessionRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TerminalHub> _log;

    public TerminalHub(SessionRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<TerminalHub>();
    }

    public async Task Connect(ConnectRequest request)
    {
        _log.LogInformation("Connect from {ConnId} → {Host}:{Port} ccsid={Ccsid}",
            Context.ConnectionId, request.Host, request.Port, request.Ccsid);

        await _registry.RemoveAsync(Context.ConnectionId);

        var session = new Tn5250Session(request.Host, request.Port, request.Ccsid, _loggerFactory);
        var caller = Clients.Caller;
        session.ScreenChanged += () => _ = PushScreenAsync(caller, session);
        session.Disconnected += () => _ = caller.SendAsync("HostDisconnected");

        try
        {
            await session.StartAsync(Context.ConnectionAborted);
            _registry.Add(Context.ConnectionId, session);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Connect failed for {ConnId}", Context.ConnectionId);
            await session.DisposeAsync();
            throw;
        }
    }

    public async Task SendKey(KeyInput input)
    {
        if (!_registry.TryGet(Context.ConnectionId, out var session))
            throw new HubException("No active session. Click Connect.");
        if (!session.IsConnected)
            throw new HubException("Host has closed the connection. Reconnect to continue.");

        try
        {
            if (input.AidKey is { } aidName && Enum.TryParse<Aid>(aidName, ignoreCase: true, out var aid))
            {
                var fields = (input.ModifiedFields ?? Array.Empty<ModifiedFieldDto>())
                    .Select(f => new ModifiedField(f.Row, f.Col, f.Text))
                    .ToArray();
                await session.SendKeyAsync(aid, fields, input.CursorRow, input.CursorCol, Context.ConnectionAborted);
            }
            else if (!string.IsNullOrEmpty(input.Text))
            {
                await session.SendTextAsync(input.Text, Context.ConnectionAborted);
            }
        }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "Write to host failed (connection lost)");
            throw new HubException("Connection to host lost. Reconnect to continue.");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _registry.RemoveAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private static async Task PushScreenAsync(IClientProxy client, Tn5250Session session)
    {
        var snap = session.Screen.Snapshot();
        var cells = new WireCell[snap.Length];
        for (int i = 0; i < snap.Length; i++)
        {
            cells[i] = new WireCell(snap[i].Char.ToString(), snap[i].Attribute);
        }

        var fieldsList = session.Screen.Fields;
        var fields = new WireField[fieldsList.Count];
        for (int i = 0; i < fieldsList.Count; i++)
        {
            var f = fieldsList[i];
            fields[i] = new WireField(f.Row, f.Col, f.Length, f.IsInput, f.IsNonDisplay, f.IsBypass, f.IsNumeric);
        }

        var update = new ScreenUpdate(
            session.Screen.Rows,
            session.Screen.Cols,
            session.Screen.CursorRow,
            session.Screen.CursorCol,
            cells,
            fields);

        await client.SendAsync("ScreenUpdate", update);
    }
}
