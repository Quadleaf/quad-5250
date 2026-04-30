import { useCallback, useEffect, useRef, useState } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import { Terminal } from "./Terminal";
import { useTerminal } from "./useTerminal";
import { THEMES, type ThemeName } from "./attrColor";
import type { WireField } from "./types";
import "./App.css";

const THEME_STORAGE_KEY = "quad5250.theme";
const HOST_STORAGE_KEY = "quad5250.host";
const PORT_STORAGE_KEY = "quad5250.port";
// Per-host credentials. localStorage is plaintext and accessible to any JS
// running on the page — see README before pointing this at production hosts.
const credsKey = (host: string, port: number) => `quad5250.creds.${host}:${port}`;

interface SavedCreds { username: string; password: string; }

function loadTheme(): ThemeName {
  const v = localStorage.getItem(THEME_STORAGE_KEY);
  return v === "light" || v === "dark" || v === "green" ? v : "green";
}

function loadPort(): number {
  const v = Number(localStorage.getItem(PORT_STORAGE_KEY));
  return Number.isFinite(v) && v > 0 ? v : 23;
}

function loadCreds(host: string, port: number): SavedCreds | null {
  const raw = localStorage.getItem(credsKey(host, port));
  if (!raw) return null;
  try {
    const v = JSON.parse(raw);
    return typeof v?.username === "string" && typeof v?.password === "string" ? v : null;
  } catch { return null; }
}

const usernameField = (fields: WireField[]) =>
  fields.find(f => f.isInput && !f.isBypass && !f.isNonDisplay);

const passwordField = (fields: WireField[]) =>
  fields.find(f => f.isInput && f.isNonDisplay);

const readField = (field: WireField, typed: Map<number, string>, cols: number) => {
  let text = "";
  for (let i = 0; i < field.length; i++) {
    const ch = typed.get(field.row * cols + field.col + i);
    if (ch === undefined) break;
    text += ch;
  }
  return text;
};

function App() {
  const {
    screen, state, error, cursor, typed, cellFlags,
    connect, typeChar, backspace, tabFwd, tabBack, moveCursor, sendAid,
    populateField,
  } = useTerminal();
  const [host, setHost] = useState(() => localStorage.getItem(HOST_STORAGE_KEY) ?? "");
  const [port, setPort] = useState<number>(loadPort);
  const [themeName, setThemeName] = useState<ThemeName>(loadTheme);
  const [remember, setRemember] = useState(false);

  // Refs survive renders without re-triggering effects, so we can stash the
  // current host and pending credential fill across the connect → screen
  // update boundary.
  const activeHostRef = useRef<{ host: string; port: number } | null>(null);
  const credsToFillRef = useRef<SavedCreds | null>(null);

  const onConnect = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!host) return;
    localStorage.setItem(HOST_STORAGE_KEY, host);
    localStorage.setItem(PORT_STORAGE_KEY, String(port));
    activeHostRef.current = { host, port };
    credsToFillRef.current = loadCreds(host, port);
    await connect({ host, port });
  };

  const onThemeChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const v = e.target.value as ThemeName;
    setThemeName(v);
    localStorage.setItem(THEME_STORAGE_KEY, v);
  };

  // Auto-fill saved creds the first time we see a screen with both a regular
  // input field and a non-display input field (the canonical sign-on layout).
  // One-shot per connect — clearing the ref prevents re-filling after the
  // user clears the field manually.
  useEffect(() => {
    const pending = credsToFillRef.current;
    if (!pending) return;
    const u = usernameField(screen.fields);
    const p = passwordField(screen.fields);
    if (!u || !p) return;
    populateField(u, pending.username);
    populateField(p, pending.password);
    credsToFillRef.current = null;
  }, [screen.fields, populateField]);

  // Wrap sendAid so we can persist the typed credentials right before they
  // get shipped to the host. We only save when both fields have content;
  // hitting Enter on a half-typed form shouldn't clobber a working entry.
  const wrappedSendAid = useCallback((aid: string) => {
    if (remember && activeHostRef.current) {
      const u = usernameField(screen.fields);
      const p = passwordField(screen.fields);
      if (u && p) {
        const username = readField(u, typed, screen.cols);
        const password = readField(p, typed, screen.cols);
        if (username && password) {
          localStorage.setItem(
            credsKey(activeHostRef.current.host, activeHostRef.current.port),
            JSON.stringify({ username, password }),
          );
        }
      }
    }
    sendAid(aid);
  }, [remember, screen.fields, screen.cols, typed, sendAid]);

  const onForget = () => {
    if (!activeHostRef.current) return;
    localStorage.removeItem(
      credsKey(activeHostRef.current.host, activeHostRef.current.port),
    );
  };

  return (
    <div className="app">
      <header className="toolbar">
        <form onSubmit={onConnect}>
          <input
            placeholder="host"
            value={host}
            onChange={(e) => setHost(e.target.value)}
          />
          <input
            type="number"
            value={port}
            onChange={(e) => setPort(Number(e.target.value))}
            style={{ width: 70 }}
          />
          <button type="submit" disabled={state !== HubConnectionState.Connected}>
            Connect
          </button>
        </form>
        <label className="checkbox" title="Save typed username and password to browser storage on Enter">
          <input
            type="checkbox"
            checked={remember}
            onChange={(e) => setRemember(e.target.checked)}
          />
          Remember credentials
        </label>
        <button type="button" onClick={onForget} title="Remove saved credentials for the current host">
          Forget
        </button>
        <select value={themeName} onChange={onThemeChange} aria-label="Theme">
          <option value="green">Green</option>
          <option value="dark">Dark</option>
          <option value="light">Light</option>
        </select>
        <span className="status">hub: {state}</span>
      </header>

      {error && <div className="error">{error}</div>}

      <Terminal
        screen={screen}
        cursor={cursor}
        typed={typed}
        cellFlags={cellFlags}
        theme={THEMES[themeName]}
        onChar={typeChar}
        onBackspace={backspace}
        onTabFwd={tabFwd}
        onTabBack={tabBack}
        onMove={moveCursor}
        onAid={wrappedSendAid}
      />
    </div>
  );
}

export default App;
