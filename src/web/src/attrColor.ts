// 5250 attribute byte → (foreground, background) color.
//
// The 5250 attribute byte takes values 0x20-0x3F (32-63). Each value picks a
// foreground/background pair plus optional underline/blink. Layout cribbed
// from tn5250j's ATTR_32 … ATTR_63 constants — see TN5250jConstants.java.

export interface AttrStyle {
  fg: string;
  bg: string;
  underline: boolean;
  blink: boolean;
  hidden: boolean;
}

const GREEN = "#2bef2b";
const WHITE = "#e8e8e8";
const RED = "#ef4d4d";
const CYAN = "#33d6d6";
const YELLOW = "#e6c84a";
const MAGENTA = "#d678d6";
const BLUE = "#4d89ef";
const BLACK = "#000";

const TABLE: Record<number, AttrStyle> = {
  0x20: { fg: GREEN, bg: BLACK, underline: false, blink: false, hidden: false },
  0x21: { fg: BLACK, bg: GREEN, underline: false, blink: false, hidden: false },
  0x22: { fg: WHITE, bg: BLACK, underline: false, blink: false, hidden: false },
  0x23: { fg: BLACK, bg: WHITE, underline: false, blink: false, hidden: false },
  0x24: { fg: GREEN, bg: BLACK, underline: true, blink: false, hidden: false },
  0x25: { fg: BLACK, bg: GREEN, underline: true, blink: false, hidden: false },
  0x26: { fg: WHITE, bg: BLACK, underline: true, blink: false, hidden: false },
  0x27: { fg: BLACK, bg: BLACK, underline: false, blink: false, hidden: true },
  0x28: { fg: RED, bg: BLACK, underline: false, blink: false, hidden: false },
  0x29: { fg: BLACK, bg: RED, underline: false, blink: false, hidden: false },
  0x2a: { fg: RED, bg: BLACK, underline: false, blink: true, hidden: false },
  0x2b: { fg: BLACK, bg: RED, underline: false, blink: true, hidden: false },
  0x2c: { fg: RED, bg: BLACK, underline: true, blink: false, hidden: false },
  0x2d: { fg: BLACK, bg: RED, underline: true, blink: false, hidden: false },
  0x2e: { fg: RED, bg: BLACK, underline: true, blink: true, hidden: false },
  0x2f: { fg: BLACK, bg: BLACK, underline: false, blink: false, hidden: true },
  0x30: { fg: CYAN, bg: BLACK, underline: false, blink: false, hidden: false },
  0x31: { fg: BLACK, bg: CYAN, underline: false, blink: false, hidden: false },
  0x32: { fg: YELLOW, bg: BLACK, underline: false, blink: false, hidden: false },
  0x33: { fg: BLACK, bg: YELLOW, underline: false, blink: false, hidden: false },
  0x34: { fg: CYAN, bg: BLACK, underline: true, blink: false, hidden: false },
  0x35: { fg: BLACK, bg: CYAN, underline: true, blink: false, hidden: false },
  0x36: { fg: YELLOW, bg: BLACK, underline: true, blink: false, hidden: false },
  0x37: { fg: BLACK, bg: BLACK, underline: false, blink: false, hidden: true },
  0x38: { fg: MAGENTA, bg: BLACK, underline: false, blink: false, hidden: false },
  0x39: { fg: BLACK, bg: MAGENTA, underline: false, blink: false, hidden: false },
  0x3a: { fg: BLUE, bg: BLACK, underline: false, blink: false, hidden: false },
  0x3b: { fg: BLACK, bg: BLUE, underline: false, blink: false, hidden: false },
  0x3c: { fg: MAGENTA, bg: BLACK, underline: true, blink: false, hidden: false },
  0x3d: { fg: BLACK, bg: MAGENTA, underline: true, blink: false, hidden: false },
  0x3e: { fg: BLUE, bg: BLACK, underline: true, blink: false, hidden: false },
  0x3f: { fg: BLACK, bg: BLACK, underline: false, blink: false, hidden: true },
};

const DEFAULT: AttrStyle = TABLE[0x20];

export function attrStyle(attr: number): AttrStyle {
  return TABLE[attr] ?? DEFAULT;
}
