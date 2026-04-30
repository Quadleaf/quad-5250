import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import type { ConnectRequest, ModifiedField, ScreenUpdate, WireField } from "./types";

const HUB_URL = import.meta.env.VITE_HUB_URL ?? "http://localhost:5200/hubs/terminal";

const blankScreen = (rows = 24, cols = 80): ScreenUpdate => ({
  rows,
  cols,
  cursorRow: 0,
  cursorCol: 0,
  cells: Array.from({ length: rows * cols }, () => ({ ch: " ", attr: 0x20 })),
  fields: [],
});

interface Cursor { row: number; col: number; }

const fieldAt = (fields: WireField[], row: number, col: number) =>
  fields.find(f => f.isInput && !f.isBypass && f.row === row && col >= f.col && col < f.col + f.length);

const firstInputField = (fields: WireField[]) =>
  fields.find(f => f.isInput && !f.isBypass);

const nextInputField = (fields: WireField[], row: number, col: number) => {
  const inputs = fields.filter(f => f.isInput && !f.isBypass);
  const after = inputs.find(f => f.row > row || (f.row === row && f.col > col));
  return after ?? inputs[0];
};

const prevInputField = (fields: WireField[], row: number, col: number) => {
  const inputs = fields.filter(f => f.isInput && !f.isBypass);
  const before = [...inputs].reverse().find(f => f.row < row || (f.row === row && f.col < col));
  return before ?? inputs[inputs.length - 1];
};

export function useTerminal() {
  const [screen, setScreen] = useState<ScreenUpdate>(() => blankScreen());
  const [state, setState] = useState<HubConnectionState>(HubConnectionState.Disconnected);
  const [error, setError] = useState<string | null>(null);
  const [cursor, setCursor] = useState<Cursor>({ row: 0, col: 0 });
  // position (row*cols+col) -> typed character
  const [typed, setTyped] = useState<Map<number, string>>(() => new Map());
  const connRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();

    conn.on("ScreenUpdate", (u: ScreenUpdate) => {
      setScreen(u);
      setTyped(new Map());
      // If host put cursor at (0,0), pre-position to first input field.
      if (u.cursorRow === 0 && u.cursorCol === 0) {
        const first = firstInputField(u.fields);
        setCursor(first ? { row: first.row, col: first.col } : { row: 0, col: 0 });
      } else {
        setCursor({ row: u.cursorRow, col: u.cursorCol });
      }
    });
    conn.on("HostDisconnected", () => {
      setError("Host closed the connection. Click Connect to reconnect.");
    });
    conn.onclose((err) => {
      setState(conn.state);
      if (err) setError(`Connection closed: ${err.message ?? err}`);
    });
    conn.onreconnecting(() => setState(conn.state));
    conn.onreconnected(() => { setState(conn.state); setError(null); });

    connRef.current = conn;
    conn.start()
      .then(() => { setState(conn.state); setError(null); })
      .catch((err) => {
        console.error("SignalR start failed", err);
        setState(conn.state);
        setError(`Hub start failed: ${err.message ?? err}`);
      });

    return () => {
      conn.stop();
      connRef.current = null;
    };
  }, []);

  const connect = useCallback(async (req: ConnectRequest) => {
    const conn = connRef.current;
    if (!conn) { setError("No SignalR connection."); return; }
    if (conn.state !== HubConnectionState.Connected) {
      setError(`SignalR not connected (state: ${conn.state})`); return;
    }
    try { await conn.invoke("Connect", req); setError(null); }
    catch (err) {
      const msg = (err as Error).message ?? String(err);
      setError(`Connect failed: ${msg}`);
    }
  }, []);

  const typeChar = useCallback((ch: string) => {
    const f = fieldAt(screen.fields, cursor.row, cursor.col);
    if (!f) return;
    setTyped(prev => {
      const next = new Map(prev);
      next.set(cursor.row * screen.cols + cursor.col, ch);
      return next;
    });
    const newCol = cursor.col + 1;
    if (newCol < f.col + f.length) {
      setCursor({ row: cursor.row, col: newCol });
    } else {
      const nf = nextInputField(screen.fields, cursor.row, cursor.col);
      if (nf) setCursor({ row: nf.row, col: nf.col });
    }
  }, [screen, cursor]);

  const backspace = useCallback(() => {
    const f = fieldAt(screen.fields, cursor.row, cursor.col);
    if (!f) return;
    if (cursor.col > f.col) {
      const newCol = cursor.col - 1;
      setTyped(prev => {
        const next = new Map(prev);
        next.delete(cursor.row * screen.cols + newCol);
        return next;
      });
      setCursor({ row: cursor.row, col: newCol });
    }
  }, [screen, cursor]);

  const tabFwd = useCallback(() => {
    const nf = nextInputField(screen.fields, cursor.row, cursor.col);
    if (nf) setCursor({ row: nf.row, col: nf.col });
  }, [screen, cursor]);

  const tabBack = useCallback(() => {
    const pf = prevInputField(screen.fields, cursor.row, cursor.col);
    if (pf) setCursor({ row: pf.row, col: pf.col });
  }, [screen, cursor]);

  const moveCursor = useCallback((dr: number, dc: number) => {
    setCursor(c => ({
      row: Math.max(0, Math.min(screen.rows - 1, c.row + dr)),
      col: Math.max(0, Math.min(screen.cols - 1, c.col + dc)),
    }));
  }, [screen.rows, screen.cols]);

  /**
   * Programmatically fill an input field with text — used for credential
   * auto-fill. Each character lands in a cell of the field starting at its
   * leftmost column; extra characters past the field length are dropped.
   */
  const populateField = useCallback((field: WireField, text: string) => {
    setTyped(prev => {
      const next = new Map(prev);
      const max = Math.min(text.length, field.length);
      for (let i = 0; i < max; i++) {
        next.set(field.row * screen.cols + field.col + i, text[i]);
      }
      return next;
    });
  }, [screen.cols]);

  const sendAid = useCallback(async (aidKey: string) => {
    const conn = connRef.current;
    if (!conn || conn.state !== HubConnectionState.Connected) return;

    // Collect modified fields. Send the full field width so spaces in the
    // middle are preserved; trim trailing untyped.
    const modified: ModifiedField[] = [];
    for (const f of screen.fields) {
      if (!f.isInput) continue;
      const cells: string[] = [];
      let lastIdx = -1;
      for (let c = 0; c < f.length; c++) {
        const t = typed.get(f.row * screen.cols + (f.col + c));
        cells.push(t ?? " ");
        if (t !== undefined) lastIdx = c;
      }
      if (lastIdx >= 0) {
        modified.push({ row: f.row, col: f.col, text: cells.slice(0, lastIdx + 1).join("") });
      }
    }

    try {
      await conn.invoke("SendKey", {
        text: null,
        aidKey,
        modifiedFields: modified,
        cursorRow: cursor.row,
        cursorCol: cursor.col,
      });
      setError(null);
    } catch (err) {
      const msg = (err as Error).message ?? String(err);
      console.error("SendKey failed", err);
      setError(`Send failed: ${msg}`);
    }
  }, [screen, typed, cursor]);

  // Build a position→cell-flags map once per screen.
  const cellFlags = useMemo(() => {
    const map = new Map<number, { input: boolean; nonDisplay: boolean }>();
    for (const f of screen.fields) {
      if (!f.isInput) continue;
      for (let c = 0; c < f.length; c++) {
        const col = f.col + c;
        if (col >= screen.cols) break;
        map.set(f.row * screen.cols + col, { input: true, nonDisplay: f.isNonDisplay });
      }
    }
    return map;
  }, [screen.fields, screen.cols]);

  return {
    screen,
    state,
    error,
    cursor,
    typed,
    cellFlags,
    connect,
    typeChar,
    backspace,
    tabFwd,
    tabBack,
    moveCursor,
    sendAid,
    populateField,
  };
}
