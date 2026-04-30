// 5250 attribute byte → (foreground, background) color.
//
// Layout cribbed from tn5250j's ATTR_32 … ATTR_63 (TN5250jConstants.java).
// Attrs 0x20-0x27 are "monochrome" (green/white/black on classic terminals)
// and theme-dependent here; 0x28-0x3F carry meaningful color information
// (red error fields, blue separators, etc.) and stay constant across themes.

export interface AttrStyle {
  fg: string;
  bg: string;
  underline: boolean;
  blink: boolean;
  hidden: boolean;
}

export type ThemeName = "green" | "light" | "dark";

export interface Theme {
  name: ThemeName;
  /** Terminal background. Used for cells with no explicit attribute bg. */
  bg: string;
  /** Default foreground (for attrs that classically used green). */
  fg: string;
  /** High-intensity / "white" foreground variant. */
  highlight: string;
  /** Cursor block color. */
  cursor: string;
}

export const THEMES: Record<ThemeName, Theme> = {
  green: { name: "green", bg: "#000", fg: "#2bef2b", highlight: "#e8e8e8", cursor: "#2bef2b" },
  dark: { name: "dark", bg: "#000", fg: "#e8e8e8", highlight: "#ffffff", cursor: "#e8e8e8" },
  light: { name: "light", bg: "#ffffff", fg: "#1a1a1a", highlight: "#000000", cursor: "#1a1a1a" },
};

// Saturated colors used for non-monochrome attribute slots — kept constant
// across themes so red/blue/etc. retain their meaning.
const RED = "#ef4d4d";
const CYAN = "#33d6d6";
const YELLOW = "#e6c84a";
const MAGENTA = "#d678d6";
const BLUE = "#4d89ef";

export function attrStyle(attr: number, theme: Theme): AttrStyle {
  const fg = theme.fg;
  const bg = theme.bg;
  const hi = theme.highlight;

  switch (attr) {
    // Monochrome family — uses theme colors. The "underline" flag on input
    // attrs (0x24-0x26) is intentionally dropped: input fields already get
    // their visual underline from the .field border-bottom on every cell of
    // the field, and the attribute byte cell is a clean space — drawing
    // text-decoration on top would put a stray underscore before the field.
    case 0x20: return { fg, bg, underline: false, blink: false, hidden: false };
    case 0x21: return { fg: bg, bg: fg, underline: false, blink: false, hidden: false };
    case 0x22: return { fg: hi, bg, underline: false, blink: false, hidden: false };
    case 0x23: return { fg: bg, bg: hi, underline: false, blink: false, hidden: false };
    case 0x24: return { fg, bg, underline: false, blink: false, hidden: false };
    case 0x25: return { fg: bg, bg: fg, underline: false, blink: false, hidden: false };
    case 0x26: return { fg: hi, bg, underline: false, blink: false, hidden: false };
    case 0x27: return { fg: bg, bg, underline: false, blink: false, hidden: true };

    // Red.
    case 0x28: return { fg: RED, bg, underline: false, blink: false, hidden: false };
    case 0x29: return { fg: bg, bg: RED, underline: false, blink: false, hidden: false };
    case 0x2a: return { fg: RED, bg, underline: false, blink: true, hidden: false };
    case 0x2b: return { fg: bg, bg: RED, underline: false, blink: true, hidden: false };
    case 0x2c: return { fg: RED, bg, underline: true, blink: false, hidden: false };
    case 0x2d: return { fg: bg, bg: RED, underline: true, blink: false, hidden: false };
    case 0x2e: return { fg: RED, bg, underline: true, blink: true, hidden: false };
    case 0x2f: return { fg: bg, bg, underline: false, blink: false, hidden: true };

    // Cyan / yellow.
    case 0x30: return { fg: CYAN, bg, underline: false, blink: false, hidden: false };
    case 0x31: return { fg: bg, bg: CYAN, underline: false, blink: false, hidden: false };
    case 0x32: return { fg: YELLOW, bg, underline: false, blink: false, hidden: false };
    case 0x33: return { fg: bg, bg: YELLOW, underline: false, blink: false, hidden: false };
    case 0x34: return { fg: CYAN, bg, underline: true, blink: false, hidden: false };
    case 0x35: return { fg: bg, bg: CYAN, underline: true, blink: false, hidden: false };
    case 0x36: return { fg: YELLOW, bg, underline: true, blink: false, hidden: false };
    case 0x37: return { fg: bg, bg, underline: false, blink: false, hidden: true };

    // Magenta / blue.
    case 0x38: return { fg: MAGENTA, bg, underline: false, blink: false, hidden: false };
    case 0x39: return { fg: bg, bg: MAGENTA, underline: false, blink: false, hidden: false };
    case 0x3a: return { fg: BLUE, bg, underline: false, blink: false, hidden: false };
    case 0x3b: return { fg: bg, bg: BLUE, underline: false, blink: false, hidden: false };
    case 0x3c: return { fg: MAGENTA, bg, underline: true, blink: false, hidden: false };
    case 0x3d: return { fg: bg, bg: MAGENTA, underline: true, blink: false, hidden: false };
    case 0x3e: return { fg: BLUE, bg, underline: true, blink: false, hidden: false };
    case 0x3f: return { fg: bg, bg, underline: false, blink: false, hidden: true };

    default: return { fg, bg, underline: false, blink: false, hidden: false };
  }
}
