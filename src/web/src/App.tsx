import { useState } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import { Terminal } from "./Terminal";
import { useTerminal } from "./useTerminal";
import { THEMES, type ThemeName } from "./attrColor";
import "./App.css";

const THEME_STORAGE_KEY = "quad5250.theme";
const HOST_STORAGE_KEY = "quad5250.host";
const PORT_STORAGE_KEY = "quad5250.port";

function loadTheme(): ThemeName {
  const v = localStorage.getItem(THEME_STORAGE_KEY);
  return v === "light" || v === "dark" || v === "green" ? v : "green";
}

function loadPort(): number {
  const v = Number(localStorage.getItem(PORT_STORAGE_KEY));
  return Number.isFinite(v) && v > 0 ? v : 23;
}

function App() {
  const {
    screen, state, error, cursor, typed, cellFlags,
    connect, typeChar, backspace, tabFwd, tabBack, moveCursor, sendAid,
  } = useTerminal();
  const [host, setHost] = useState(() => localStorage.getItem(HOST_STORAGE_KEY) ?? "");
  const [port, setPort] = useState<number>(loadPort);
  const [themeName, setThemeName] = useState<ThemeName>(loadTheme);

  const onConnect = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!host) return;
    localStorage.setItem(HOST_STORAGE_KEY, host);
    localStorage.setItem(PORT_STORAGE_KEY, String(port));
    await connect({ host, port });
  };

  const onThemeChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const v = e.target.value as ThemeName;
    setThemeName(v);
    localStorage.setItem(THEME_STORAGE_KEY, v);
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
        onAid={sendAid}
      />
    </div>
  );
}

export default App;
