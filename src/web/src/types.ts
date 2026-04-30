export interface WireCell {
  ch: string;
  attr: number;
}

export interface WireField {
  row: number;
  col: number;
  length: number;
  isInput: boolean;
  isNonDisplay: boolean;
  isBypass: boolean;
  isNumeric: boolean;
}

export interface ScreenUpdate {
  rows: number;
  cols: number;
  cursorRow: number;
  cursorCol: number;
  cells: WireCell[];
  fields: WireField[];
}

export interface ConnectRequest {
  host: string;
  port?: number;
  ccsid?: number;
}

export interface ModifiedField {
  row: number;
  col: number;
  text: string;
}
