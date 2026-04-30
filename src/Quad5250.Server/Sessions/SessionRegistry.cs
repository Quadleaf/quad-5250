using System.Collections.Concurrent;
using Quad5250.Protocol.Session;

namespace Quad5250.Server.Sessions;

/// <summary>
/// Maps a SignalR connection id to its <see cref="Tn5250Session"/>.
/// One session per connection; cleaned up on disconnect.
/// </summary>
public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<string, Tn5250Session> _sessions = new();

    public bool TryGet(string connectionId, out Tn5250Session session)
        => _sessions.TryGetValue(connectionId, out session!);

    public void Add(string connectionId, Tn5250Session session)
        => _sessions[connectionId] = session;

    public async Task RemoveAsync(string connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var session))
        {
            await session.DisposeAsync();
        }
    }
}
