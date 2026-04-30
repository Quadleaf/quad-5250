import { useCallback, useEffect, useRef } from "react";
import type { ScreenUpdate } from "./types";

interface CellFlags { input: boolean; nonDisplay: boolean; }

interface Props {
  screen: ScreenUpdate;
  cursor: { row: number; col: number };
  typed: Map<number, string>;
  cellFlags: Map<number, CellFlags>;
  onChar: (ch: string) => void;
  onBackspace: () => void;
  onTabFwd: () => void;
  onTabBack: () => void;
  onMove: (dr: number, dc: number) => void;
  onAid: (aid: string) => void;
}

const KEY_TO_AID: Record<string, string> = {
  Enter: "Enter",
  Escape: "Clear",
  F1: "F1", F2: "F2", F3: "F3", F4: "F4", F5: "F5", F6: "F6",
  F7: "F7", F8: "F8", F9: "F9", F10: "F10", F11: "F11", F12: "F12",
  PageUp: "RollDown",
  PageDown: "RollUp",
};

export function Terminal({
  screen, cursor, typed, cellFlags,
  onChar, onBackspace, onTabFwd, onTabBack, onMove, onAid,
}: Props) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => { ref.current?.focus(); }, []);

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      const aid = KEY_TO_AID[e.key];
      if (aid) { e.preventDefault(); onAid(aid); return; }

      switch (e.key) {
        case "Backspace": e.preventDefault(); onBackspace(); return;
        case "Tab": e.preventDefault(); e.shiftKey ? onTabBack() : onTabFwd(); return;
        case "ArrowLeft": e.preventDefault(); onMove(0, -1); return;
        case "ArrowRight": e.preventDefault(); onMove(0, 1); return;
        case "ArrowUp": e.preventDefault(); onMove(-1, 0); return;
        case "ArrowDown": e.preventDefault(); onMove(1, 0); return;
      }

      if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
        e.preventDefault();
        onChar(e.key);
      }
    },
    [onChar, onBackspace, onTabFwd, onTabBack, onMove, onAid],
  );

  const rows: React.ReactNode[] = [];
  for (let r = 0; r < screen.rows; r++) {
    const cells: React.ReactNode[] = [];
    for (let c = 0; c < screen.cols; c++) {
      const idx = r * screen.cols + c;
      const cell = screen.cells[idx];
      const flags = cellFlags.get(idx);
      const isCursor = r === cursor.row && c === cursor.col;
      const typedCh = typed.get(idx);

      let ch = typedCh ?? cell?.ch ?? " ";
      // Mask non-display cells that the user typed into.
      if (typedCh !== undefined && flags?.nonDisplay) ch = "•";

      const className = [
        "cell",
        isCursor && "cursor",
        flags?.input && "field",
        flags?.nonDisplay && "non-display",
      ].filter(Boolean).join(" ");
      cells.push(
        <span key={c} className={className}>
          {ch === " " ? " " : ch}
        </span>,
      );
    }
    rows.push(<div key={r} className="row">{cells}</div>);
  }

  return (
    <div ref={ref} className="terminal" tabIndex={0} onKeyDown={onKeyDown}>
      {rows}
    </div>
  );
}
