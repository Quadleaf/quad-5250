import { useCallback, useEffect, useRef } from "react";
import { attrStyle, type Theme } from "./attrColor";
import type { ScreenUpdate } from "./types";

interface CellFlags { input: boolean; nonDisplay: boolean; }

interface Props {
  screen: ScreenUpdate;
  cursor: { row: number; col: number };
  typed: Map<number, string>;
  cellFlags: Map<number, CellFlags>;
  theme: Theme;
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
  screen, cursor, typed, cellFlags, theme,
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

      const isTypedBullet = typedCh !== undefined && flags?.nonDisplay;
      let ch = typedCh ?? cell?.ch ?? " ";
      if (isTypedBullet) ch = "•";

      const style = attrStyle(cell?.attr ?? 0x20, theme);
      const className = [
        "cell",
        isCursor && "cursor",
        flags?.input && "field",
        // Input fields already get an underline via the .field border-bottom;
        // suppress the text-decoration here so attrs like 0x24 (input+underline)
        // don't render with two stacked underlines under typed text.
        style.underline && !flags?.input && "underline",
        style.blink && "blink",
      ].filter(Boolean).join(" ");
      const inlineStyle: React.CSSProperties = isCursor
        ? { background: theme.cursor, color: theme.bg }
        // Typed bullets in password fields override the attr (which is 0x27
        // / hidden) so the user can see they typed something. We keep the
        // cell at full opacity so the .field border-bottom underline stays
        // crisp; the bullet glyph itself is dimmed via a child span below.
        : isTypedBullet
          ? { color: theme.fg }
          : style.hidden
            // Hidden cells in an input field (e.g. empty Password, attr 0x27)
            // need a visible currentColor so the .field border-bottom shows;
            // the cell content is a space and stays invisible. Hidden cells
            // outside fields (output text the host wants suppressed) get
            // color = bg so they truly disappear.
            ? flags?.input
              ? { color: theme.fg }
              : { color: style.bg }
            // Only paint background when it differs from the terminal bg, so
            // adjacent default cells don't get overlapping backgrounds.
            : { color: style.fg, background: style.bg === theme.bg ? undefined : style.bg };
      cells.push(
        <span key={c} className={className} style={inlineStyle}>
          {isTypedBullet
            ? <span style={{ opacity: 0.7 }}>•</span>
            : ch === " " ? " " : ch}
        </span>,
      );
    }
    rows.push(<div key={r} className="row">{cells}</div>);
  }

  return (
    <div
      ref={ref}
      className="terminal"
      tabIndex={0}
      onKeyDown={onKeyDown}
      style={{ background: theme.bg, color: theme.fg }}
    >
      {rows}
    </div>
  );
}
