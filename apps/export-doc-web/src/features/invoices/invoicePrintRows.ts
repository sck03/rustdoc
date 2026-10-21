import type { ApiInvoiceItemDto, ApiInvoicePrintRow } from "../../api/index.ts";

export type PrintRowSelection = { item: number; row: number };
export const maxItemPrintRows = 64;

export function itemPrintRows(item: ApiInvoiceItemDto): ApiInvoicePrintRow[] {
  return item.printRows?.length ? item.printRows : [{ kind: "Item", text: item.styleName ?? "" }];
}

export function resolvePrintRowSelection(items: ApiInvoiceItemDto[], selected: PrintRowSelection): PrintRowSelection {
  const item = Math.max(0, Math.min(selected.item, items.length - 1));
  const row = items[item] ? Math.max(0, Math.min(selected.row, itemPrintRows(items[item]).length - 1)) : 0;
  return item === selected.item && row === selected.row ? selected : { item, row };
}

export function changePrintRow(rows: ApiInvoicePrintRow[], index: number, action: "text" | "blank" | "duplicate" | "delete" | "up" | "down" | "split", after = false): ApiInvoicePrintRow[] {
  if (index < 0 || index >= rows.length) return rows;
  const next = [...rows];
  if (action === "split") {
    if (rows.length >= maxItemPrintRows || rows[index].kind !== "Item" || !rows[index].text) return rows;
    next.splice(index, 1, { kind: "Text", text: rows[index].text }, { ...rows[index], text: "" });
  } else if (action === "text" || action === "blank" || action === "duplicate") {
    if (rows.length >= maxItemPrintRows || action === "duplicate" && rows[index].kind === "Item") return rows;
    next.splice(index + Number(after || action === "duplicate"), 0, action === "duplicate" ? { ...rows[index] } : { kind: action === "text" ? "Text" : "Blank", text: "" });
  } else if (action === "delete") {
    if (rows[index].kind === "Item") return rows;
    next.splice(index, 1);
  } else {
    const target = index + (action === "up" ? -1 : 1);
    if (target < 0 || target >= rows.length) return rows;
    [next[index], next[target]] = [next[target], next[index]];
  }
  return next;
}

export function flattenPrintRows(items: ApiInvoiceItemDto[]) {
  return items.flatMap((item, itemIndex) => itemPrintRows(item).map((row, rowIndex) => ({ item, itemIndex, row, rowIndex })));
}

export function canMovePrintRow(items: ApiInvoiceItemDto[], selection: PrintRowSelection, direction: -1 | 1) {
  const item = items[selection.item];
  if (!item) return false;
  const rows = itemPrintRows(item), row = rows[selection.row];
  if (!row) return false;
  if (selection.row + direction >= 0 && selection.row + direction < rows.length) return true;
  const adjacent = items[selection.item + direction];
  return row.kind !== "Item" && Boolean(adjacent && itemPrintRows(adjacent).length < maxItemPrintRows);
}

/** Notes may cross an item boundary. Business rows always stay with their values. */
export function movePrintRow(items: ApiInvoiceItemDto[], selection: PrintRowSelection, direction: -1 | 1) {
  const current = items[selection.item];
  if (!current) return { items, selection };
  const rows = itemPrintRows(current), row = rows[selection.row];
  if (!row) return { items, selection };
  const target = selection.row + direction;
  if (target >= 0 && target < rows.length) {
    const next = changePrintRow(rows, selection.row, direction === -1 ? "up" : "down");
    return { items: items.map((item, index) => index === selection.item ? { ...item, printRows: next } : item), selection: { ...selection, row: target } };
  }
  const adjacentIndex = selection.item + direction, adjacent = items[adjacentIndex];
  if (!adjacent || row.kind === "Item") return { items, selection };
  const adjacentRows = itemPrintRows(adjacent);
  if (adjacentRows.length >= maxItemPrintRows) return { items, selection };
  const insertAt = direction === -1 ? adjacentRows.length - 1 : 1;
  const nextRows = [...adjacentRows];
  nextRows.splice(insertAt, 0, row);
  return {
    items: items.map((item, index) => index === selection.item ? { ...item, printRows: rows.filter((_, i) => i !== selection.row) }
      : index === adjacentIndex ? { ...item, printRows: nextRows } : item),
    selection: { item: adjacentIndex, row: insertAt },
  };
}

export function insertPrintNotes(rows: ApiInvoicePrintRow[], index: number, text: string) {
  const lines = text.replace(/\r\n?/g, "\n").split("\n");
  if (rows.length + lines.length > maxItemPrintRows) throw new Error(`本货项最多 ${maxItemPrintRows} 行，请分批插入到不同货项。`);
  if (lines.some(line => [...line].length > 1000 || /[\x00-\x08\x0b-\x1f\x7f]/.test(line))) throw new Error("每行最多 1000 字，不能包含控制字符。");
  const added: ApiInvoicePrintRow[] = lines.map(text => ({ kind: text ? "Text" : "Blank", text }));
  return [...rows.slice(0, index), ...added, ...rows.slice(index)];
}
