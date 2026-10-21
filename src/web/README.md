# Quad 5250 web client

The React + SignalR browser terminal. It needs the hub in `src/Quad5250.Server` running.

```powershell
npm ci
npm run dev        # http://localhost:5173, hub at http://localhost:5200 (override with VITE_HUB_URL)
npm run build
```

See the [repository README](../../README.md) for the other clients, the installer and 5250 support.
