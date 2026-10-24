export type TabularClipboardLimits = { characters: number; rows: number; columns: number; cells: number; rectangular?: boolean };

/** Shared Excel TSV reader. Quoted cells retain tabs/newlines and doubled quotes. */
export function parseTabularClipboard(text: string, limits: TabularClipboardLimits): string[][] {
  if (!text) return [];
  if (text.length > limits.characters) throw new Error("粘贴文字过长，请分批填入。");
  const rows: string[][] = [];
  let row: string[] = [], cell = "", quoted = false, closed = false, cells = 0;
  const field = () => {
    row.push(cell); cell = ""; closed = false;
    if (row.length > limits.columns || ++cells > limits.cells) throw new Error("粘贴单元格数量超过上限，请分批填入。");
  };
  const line = () => {
    field(); rows.push(row); row = [];
    if (rows.length > limits.rows) throw new Error(`粘贴内容最多 ${limits.rows} 行，请分批填入。`);
  };
  const value = text.replace(/\r\n?/g, "\n");
  for (let index = 0; index < value.length; index++) {
    const character = value[index];
    if (quoted) {
      if (character !== '"') cell += character;
      else if (value[index + 1] === '"') { cell += '"'; index++; }
      else { quoted = false; closed = true; }
    } else if (character === "\t") field();
    else if (character === "\n") line();
    else if (character === '"' && !cell && !closed) quoted = true;
    else {
      if (closed) throw new Error("带引号的单元格格式不完整，请重新复制表格文字。");
      cell += character;
    }
  }
  if (quoted) throw new Error("带引号的单元格尚未结束，请重新复制完整内容。");
  if (cell || row.length || closed || !value.endsWith("\n")) line();
  if (limits.rectangular && rows.some(row => row.length !== rows[0].length)) throw new Error("每行列数必须一致，空单元格请保留制表符。");
  return rows;
}
