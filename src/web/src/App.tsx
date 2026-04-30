import { useState } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import { Terminal } from "./Terminal";
import { useTerminal } from "./useTerminal";
import "./App.css";

function App() {
  const {
    screen, state, error, cursor, typed, cellFlags,
    connect, typeChar, backspace, tabFwd, tabBack, moveCursor, sendAid,
  } = useTerminal();
  const [host, setHost] = useState("");
  const [port, setPort] = useState(23);

  const onConnect = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!host) return;
    await connect({ host, port });
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
        <span className="status">hub: {state}</span>
      </header>

      {error && <div className="error">{error}</div>}

      <Terminal
        screen={screen}
        cursor={cursor}
        typed={typed}
        cellFlags={cellFlags}
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
