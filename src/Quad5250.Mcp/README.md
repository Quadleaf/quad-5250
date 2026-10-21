# Quad5250.Mcp

An MCP server that gives an AI agent (Claude Code, Claude Desktop) a headless
5250 session on IBM i. It's meant for research while modernizing green-screen
applications: walk the real screens, read fields and messages, and capture
screenshots for specs.

It runs over stdio and drives the same protocol library and renderer as the
desktop client, so screenshots match what an operator sees.

## Setup

The Quad 5250 installer (`build/pack.ps1`) ships the MCP server next to the
desktop client, at a path that stays the same across updates:
`%LOCALAPPDATA%\Quad5250\current\Quad5250.Mcp.exe`.

Register it with Claude Code at user scope, so host names and the user never
land in a repository:

```powershell
claude mcp add quad5250 --scope user `
  -e QUAD5250_HOSTS="dev=10.0.0.5" `
  -e QUAD5250_USER=MYUSER `
  -e QUAD5250_PASSWORD_ENV=MY_IBMI_PASSWORD `
  -- "$env:LOCALAPPDATA\Quad5250\current\Quad5250.Mcp.exe"
```

Applying a desktop update stops a running MCP server (Velopack replaces the
whole `current` folder); reconnect it in Claude Code with `/mcp`.

For development, publish somewhere else rather than pointing Claude at
`bin/Debug`, which the running server would lock against the next build:
`dotnet publish src/Quad5250.Mcp -c Release -o <folder>`.

| Variable | Meaning |
|---|---|
| `QUAD5250_HOSTS` | Allowed hosts: `name=host[:port]`, `;`-separated. Only these can be reached; leave production out unless you mean it. |
| `QUAD5250_USER` | Sign-on user profile. |
| `QUAD5250_PASSWORD_ENV` | Name of the environment variable that holds the password (default `QUAD5250_PASSWORD`). Set that variable in your user environment, not in the MCP config. |
| `QUAD5250_SCREENSHOT_DIR` | Where screenshots are saved (default `Pictures\Quad5250`). |
| `QUAD5250_AUDIT_LOG` | Action log (default `%APPDATA%\Quad5250\mcp-audit.log`). |

## Tools

| Tool | What it does |
|---|---|
| `list_hosts` | Configured host names. |
| `connect` | Open a session to a host and return the first screen. |
| `sign_on` | Fill the sign-on screen from the configured user and password variable and press Enter. |
| `read_screen` | Numbered rows, input fields (id, label, position, value, flags), F-key legend. |
| `fill_fields` | Put values into fields by id or label. Nothing is sent until a key is pressed. |
| `press_key` | Enter, F1-F24, PageUp/PageDown, Clear, Help, Print; optional cursor position first. Waits for the host to finish the next screen. |
| `wait_for_screen` | Wait longer when the host is still busy. |
| `screenshot` | PNG (returned inline and saved) or SVG; redact fields or regions; Green/Dark/Light. |
| `disconnect` | Close the session (sign off on the host first). |

## Safety

- **The password never reaches the model.** `sign_on` reads it from the
  environment inside the server. Protocol logging is off, because the
  transmit log would contain the sign-on record.
- **One sign-on attempt per host.** A rejected sign-on blocks further
  attempts until the server restarts, so a wrong password can't walk the
  profile into a `QMAXSIGN` lockout.
- **Every key press reaches the live system.** Nothing makes a 5250 session
  read-only. The host allowlist is the guard, and the server instructions ask
  the agent to stay on inquiry paths.
- **Patient data.** Screens can show PHI. Non-display fields are always masked
  in text and images. The audit log records actions, field ids and value
  lengths, never values. Redact screenshots before sharing them, and keep
  unredacted ones out of git.

## Screen completion

After an AID key the server waits until the host releases the keyboard (a
Write to Display with the unlock bit, or a Read command) and then stays quiet
for 150 ms. A screen drawn in several records is returned whole, not half
drawn.
