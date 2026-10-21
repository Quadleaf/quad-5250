# Quad 5250

A TN5250 terminal emulator for IBM i, written in .NET 10. It comes in three
frontends over one protocol library:

- **Desktop client** (WPF): a standalone terminal that connects straight to the
  IBM i, so it works over a VPN with no server in between.
- **MCP server**: a headless session that an AI agent such as Claude Code can
  drive to walk green-screen applications and capture screenshots.
- **Web client** (React + SignalR): the original browser terminal, brokered by
  an ASP.NET Core server.

## Download

Get the Windows installer from the **[latest release](https://github.com/Quadleaf/quad-5250/releases/latest)**
(`Quad5250-win-Setup.exe`; all versions are on the [Releases page](https://github.com/Quadleaf/quad-5250/releases)).
It installs the desktop client and the MCP server for the current user, with no admin rights.
The installer isn't code-signed yet, so Windows shows "Windows protected your PC" once: choose
**More info**, then **Run anyway**.

## Projects

| Project | What it is |
|---|---|
| `src/Quad5250.Protocol` | Telnet negotiation, EBCDIC, the 5250 data stream parser, the screen buffer, and operator input (`FieldEditor`). No UI dependencies. |
| `src/Quad5250.Rendering` | Draws a screen with WPF, on screen or off screen to PNG, and as SVG. Shared by the desktop client and the MCP server so screenshots match the app. |
| `src/Quad5250.Desktop` | The WPF client. |
| `src/Quad5250.Mcp` | The MCP server (stdio). See [its README](src/Quad5250.Mcp/README.md) for setup, tools and safety. |
| `src/Quad5250.Server` | SignalR hub that brokers browser sessions. |
| `src/web` | The React browser client. |
| `tests/*` | xUnit tests for the protocol library and the MCP server. |
| `build/pack.ps1` | Builds the Windows installer (desktop client + MCP server). |

## Requirements

- .NET 10 SDK. The desktop client, the MCP server and their tests need Windows, because they use WPF.
- Node.js 20+ for the web client.
- Telnet access to the IBM i (port 23), e.g. over the VPN.

## Running

**Desktop client**

```powershell
dotnet run --project src/Quad5250.Desktop
```

Enter the host, press Connect, and sign on. The connect bar remembers the host and theme in
`%APPDATA%\Quad5250\settings.json`. With **Remember credentials** checked, the user and password from the
sign-on screen are saved on Enter, encrypted for your Windows account (DPAPI), and filled in on the next
connect to that host.

| Key | 5250 function |
|---|---|
| Enter | Enter |
| F1-F12, Shift+F1-F12 | F1-F24 |
| Page Up / Page Down | Roll Down / Roll Up |
| Esc | Clear |
| Tab / Shift+Tab | Next / previous field |
| Home | First input field |
| Backspace, Delete | Edit within the field |
| Ctrl+End | Erase to end of field |
| Ctrl+V | Paste into fields |
| Ctrl+Shift+C / Ctrl+Shift+S | Copy / save a screenshot |

Clicking a cell moves the cursor; clicking an F-key legend such as `F3=Exit` presses that key. **X SYSTEM**
in the status bar means the keyboard is locked while the host works.

**Web client**

```powershell
dotnet run --project src/Quad5250.Server --launch-profile http   # hub on http://localhost:5200
cd src/web; npm ci; npm run dev                                   # http://localhost:5173
```

`VITE_HUB_URL` overrides the hub address. The browser keeps saved credentials in plain `localStorage`, so
don't use Remember credentials with production hosts there.

**MCP server**: see [src/Quad5250.Mcp/README.md](src/Quad5250.Mcp/README.md).

## Tests

```powershell
dotnet test Quad5250.slnx
```

## Installer

One Velopack installer carries both the desktop client and the MCP server. It installs per user to
`%LOCALAPPDATA%\Quad5250` without admin rights, adds Start menu and Desktop shortcuts, and updates both
apps together.

**Build locally**

```powershell
dotnet tool install -g vpk --version 1.2.161
./build/pack.ps1 -Version 1.0.0
```

The output in `artifacts/releases` is `Quad5250-win-Setup.exe` for users, plus the package and
`releases.win.json` files that an update feed serves. Pass `-UpdateFeedUrl` to bake in where installed
copies look for updates; without it they never check. To get a small delta update, pack each new version
into a folder that already holds the previous release.

**Build in GitHub Actions** (`.github/workflows/installer.yml`)

- Every pull request and manual run tests the solution, builds the installer and attaches it to the run
  (Actions > the run > Artifacts). A manual run can take a version; the default is `0.0.<run number>`.
- Pushing a tag such as `v1.2.0` does the same, then publishes a GitHub Release named "Quad 5250 1.2.0"
  with the installer and update files, including a delta from the previous release.
- The repository variable `UPDATE_FEED_URL`, when set, is baked into the build as the update feed.

Things to know:

- **Updates need a reachable feed.** The repository is private, so installed copies can't read its
  releases without a token. Point `UPDATE_FEED_URL` at a location users can reach (a file share on the
  VPN, or an Azure Blob container) and copy each release's files there.
- **The installer isn't code-signed yet.** Windows shows "Windows protected your PC" on the first
  download; users choose More info, then Run anyway. Updates that the app downloads itself don't prompt.
  `pack.ps1 -SignParams` passes signtool arguments once a certificate exists.
- **Updating stops a running MCP server**, because Velopack replaces the install folder. Reconnect it in
  Claude Code with `/mcp`. The desktop client warns about this before it restarts.

## 5250 support

Implemented, with unit tests, and exercised against a live IBM i (sign-on, menus, subfiles, pop-up windows):

- Telnet/TN5250E negotiation, the Query reply, and enhanced mode.
- Write to Display with Start of Field (field format words, field control words, output-only fields),
  Set Buffer Address, Insert Cursor, Repeat to Address, Erase to Address, and attribute bytes. A cell takes
  its attribute from the nearest attribute byte before it, bounded by the field's length.
- The keyboard unlocks on the Write to Display unlock bit or a Read command. The first control byte's
  resets apply before each write: clearing modified flags and nulling fields, and locking the keyboard.
- Read MDT responses with the modified fields, including host-set MDT, for Enter, F1-F24, Roll, Clear,
  Help and Print.
- Save Screen / Restore Screen, which pop-up windows use.
- Field rules: bypass, monocase, digits-only and signed numeric, auto-enter.

Known gaps:

- No insert mode, Dup, Field Exit or Reset keys, and no blinking text. If a host never releases the
  keyboard after a key press, reconnect.
- 24x80 only; no 27x132.
- No enhanced 5250 windows (the Create Window structured field); scroll bars and other structured-field
  orders are skipped too. On the host we test against, pop-up windows arrive as a Clear Unit followed by a
  border drawn with characters, so the screen behind a window shows blank, exactly as sent.
- The web client keeps its own typed-character buffer instead of the protocol library's `FieldEditor`. A
  partial screen write from the host clears its typed input, and editing part of a field blanks the host's
  text in the rest of it.

## Deployment of the web client

The Azure Container Apps deployment (azd + Terraform) lives on the `azure-deployment` branch.
