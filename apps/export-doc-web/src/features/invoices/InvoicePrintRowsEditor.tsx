import { useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import type { ApiInvoiceItemDto } from "../../api/index.ts";
import { InvoiceItemCellInput } from "./InvoiceItemCellInput.tsx";
import { canMovePrintRow, changePrintRow, flattenPrintRows, insertPrintNotes, itemPrintRows, maxItemPrintRows, movePrintRow, resolvePrintRowSelection, type PrintRowSelection } from "./invoicePrintRows.ts";
import { usePrintRowColumns } from "./InvoicePrintRowsColumns.tsx";

const pageSize = 50;
export type EditInvoiceItems = (update: (items: ApiInvoiceItemDto[]) => ApiInvoiceItemDto[]) => void;

export function InvoicePrintRowsEditor({ items, readOnly, onChangeItem, onEditItems, onAddItem, onDuplicateItem, onRemoveItem, onMoveItem, onUndo, onRedo, canUndo, canRedo }: {
  items: ApiInvoiceItemDto[]; readOnly: boolean;
  onChangeItem: (index: number, patch: Partial<ApiInvoiceItemDto>) => void;
  onEditItems: EditInvoiceItems; onAddItem: () => void; onDuplicateItem: (index: number) => void;
  onRemoveItem: (index: number) => void; onMoveItem: (index: number, direction: -1 | 1) => void;
  onUndo: () => void; onRedo: () => void; canUndo: boolean; canRedo: boolean;
}) {
  const [selected, setSelection] = useState<PrintRowSelection>({ item: 0, row: 0 });
  const selection = resolvePrintRowSelection(items, selected);
  const [notes, setNotes] = useState("");
  const [message, setMessage] = useState("");
  const root = useRef<HTMLDivElement>(null);
  const pendingFocus = useRef(false);
  const { columns, menu, label: columnLabel } = usePrintRowColumns();
  const entries = useMemo(() => flattenPrintRows(items), [items]);
  const pages = Math.max(1, Math.ceil(entries.length / pageSize));
  const selectedIndex = entries.findIndex(entry => entry.itemIndex === selection.item && entry.rowIndex === selection.row);
  const currentPage = Math.floor(Math.max(0, selectedIndex) / pageSize);
  const current = items[selection.item];
  const rows = current ? itemPrintRows(current) : [];
  const selectedRow = rows[selection.row];
  useLayoutEffect(() => {
    if (!pendingFocus.current) return;
    pendingFocus.current = false;
    const row = root.current?.querySelector(`tr[data-print-item="${selection.item}"][data-print-row="${selection.row}"]`);
    const target = row?.querySelector<HTMLTextAreaElement>("textarea") ?? row?.querySelector<HTMLButtonElement>("button");
    target?.focus();
  }, [items, currentPage, selection]);
  function select(next: PrintRowSelection) {
    setSelection(next);
    pendingFocus.current = true;
    setMessage("");
  }
  function changePage(next: number) {
    const entry = entries[next * pageSize];
    if (entry) select({ item: entry.itemIndex, row: entry.rowIndex });
  }
  function act(action: Parameters<typeof changePrintRow>[2], after = false) {
    if (!current || !selectedRow || readOnly) return;
    if (action === "up" || action === "down") {
      const direction = action === "up" ? -1 : 1;
      const result = movePrintRow(items, selection, direction);
      onEditItems(latest => movePrintRow(latest, selection, direction).items);
      select(result.selection);
      return;
    }
    const next = changePrintRow(rows, selection.row, action, after);
    if (next === rows) return;
    onChangeItem(selection.item, { printRows: next });
    const row = action === "delete" ? Math.min(selection.row, next.length - 1) : selection.row + Number(after || action === "duplicate");
    select({ ...selection, row });
  }
  const canMove = (direction: -1 | 1) => !readOnly && canMovePrintRow(items, selection, direction);
  function moveItem(direction: -1 | 1) {
    onMoveItem(selection.item, direction);
    select({ ...selection, item: selection.item + direction });
  }
  function insertNotes() {
    if (!current || !selectedRow || readOnly) return;
    try {
      const next = insertPrintNotes(rows, selection.row + 1, notes);
      onChangeItem(selection.item, { printRows: next });
      setNotes("");
      select({ ...selection, row: selection.row + 1 });
    } catch (error) { setMessage(error instanceof Error ? error.message : "无法插入文字。"); }
  }
  function keyDown(event: KeyboardEvent) {
    if (readOnly || event.nativeEvent.isComposing || event.keyCode === 229) return;
    if ((event.ctrlKey || event.metaKey) && !event.altKey && ["z", "y"].includes(event.key.toLowerCase())) {
      event.preventDefault();
      const redo = event.shiftKey || event.key.toLowerCase() === "y";
      if (redo ? canRedo : canUndo) (redo ? onRedo : onUndo)();
    } else if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key === "Enter") {
      event.preventDefault();
      act("text", true);
    } else if (event.altKey && !event.ctrlKey && !event.metaKey && ["ArrowUp", "ArrowDown"].includes(event.key)) {
      event.preventDefault();
      act(event.key === "ArrowUp" ? "up" : "down");
    }
  }
  return <div className="invoice-print-rows" ref={root}>
    <div className="toolbar-actions" role="toolbar" aria-label="打印行编辑">
      <button className="command-button secondary" type="button" disabled={readOnly} onClick={() => { onAddItem(); select({ item: items.length, row: 0 }); }}>新增商品</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !selectedRow || rows.length >= maxItemPrintRows} onClick={() => act("text")}>上方插入说明</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !selectedRow || rows.length >= maxItemPrintRows} onClick={() => act("text", true)}>下方插入说明</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !selectedRow || rows.length >= maxItemPrintRows} onClick={() => act("blank", true)}>下方插入空白行</button>
      <button className="command-button secondary" type="button" disabled={readOnly || selectedRow?.kind !== "Item" || !selectedRow.text || rows.length >= maxItemPrintRows} onClick={() => act("split")}>描述移为上方说明</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !selectedRow || selectedRow.kind === "Item"} onClick={() => act("delete")}>删除说明 / 空白行</button>
      <button className="command-button secondary" type="button" disabled={!canMove(-1)} onClick={() => act("up")}>上移行</button>
      <button className="command-button secondary" type="button" disabled={!canMove(1)} onClick={() => act("down")}>下移行</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !canUndo} onClick={onUndo}>撤销</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !canRedo} onClick={onRedo}>重做</button>
      {menu}
    </div>
    <div className="invoice-print-options">
    <details className="invoice-print-notes"><summary>行与货项操作</summary><div className="toolbar-actions" role="group" aria-label="当前货项操作">
      <span>{current ? `当前货项 ${selection.item + 1}` : "请选择商品"}</span>
      <button className="command-button secondary" type="button" disabled={readOnly || !selectedRow || selectedRow.kind === "Item" || rows.length >= maxItemPrintRows} onClick={() => act("duplicate")}>复制说明 / 空白行</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !current} onClick={() => { onDuplicateItem(selection.item); select({ item: selection.item + 1, row: 0 }); }}>复制货项</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !current || selection.item === 0} onClick={() => moveItem(-1)}>上移货项</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !current || selection.item === items.length - 1} onClick={() => moveItem(1)}>下移货项</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !current} onClick={() => { onRemoveItem(selection.item); select({ item: Math.max(0, Math.min(selection.item, items.length - 2)), row: 0 }); }}>删除货项</button>
      <button className="command-button secondary" type="button" disabled={readOnly || !current?.printRows?.length} onClick={() => { onChangeItem(selection.item, { printRows: [] }); select({ ...selection, row: 0 }); }}>重置本货项排版</button>
    </div></details>
    <details className="invoice-print-notes"><summary>批量插入说明与空白行</summary>
      <label>每行作为一条说明，空行保留<textarea aria-label="批量说明文字" value={notes} onChange={event => setNotes(event.target.value)} disabled={readOnly} maxLength={64000} rows={4} /></label>
      <button className="command-button secondary" type="button" disabled={readOnly || !selectedRow || !notes} onClick={insertNotes}>插入到所选行下方</button>
    </details>
    <details className="invoice-print-notes"><summary>排版说明与快捷键</summary>
      <p>按上下顺序输出，描述可留空或换行。说明与空白行不计合计，可跨货项移动。移动、复制货项时连同其说明处理。</p>
      <p>Ctrl/Cmd + Enter 在下方插入说明，Alt + ↑ / ↓ 移动行，Ctrl/Cmd + Z 撤销。自定义明细表可在设计器中选择“按录入行打印的说明列”。</p>
    </details>
    </div>
    {message && <p role="alert">{message}</p>}
    <div className="invoice-print-rows-scroll" onKeyDown={keyDown}>
      <table aria-label="按录入行打印明细"><thead><tr><th scope="col">行</th><th scope="col">类型</th><th scope="col">打印描述</th>{columns.map(column => <th scope="col" key={column.field}>{columnLabel(column.field, column.header)}</th>)}</tr></thead>
        <tbody>{entries.slice(currentPage * pageSize, (currentPage + 1) * pageSize).map(({ item, itemIndex, row, rowIndex }, index) => {
          const selected = selection.item === itemIndex && selection.row === rowIndex;
          const label = `打印第 ${currentPage * pageSize + index + 1} 行`;
          const focus = () => setSelection(current => current.item === itemIndex && current.row === rowIndex ? current : { item: itemIndex, row: rowIndex });
          return <tr key={`${itemIndex}:${rowIndex}`} data-print-item={itemIndex} data-print-row={rowIndex} data-selected={selected} onFocus={focus} onClick={focus}>
            <th scope="row"><button type="button" aria-label={`选择${label}`} aria-pressed={selected} onClick={focus}>{currentPage * pageSize + index + 1}</button></th>
            <td>{row.kind === "Item" ? `货项 ${itemIndex + 1}` : row.kind === "Text" ? "说明" : "空白"}</td>
            <td>{row.kind === "Blank" ? <span aria-label="保留空白行">&nbsp;</span> : <textarea aria-label={`${label}描述`} value={row.text} rows={Math.max(1, Math.min(6, row.text.split("\n").length))} maxLength={1000} disabled={readOnly}
              onChange={event => onChangeItem(itemIndex, { printRows: itemPrintRows(item).map((entry, i) => i === rowIndex ? { ...entry, text: event.target.value } : entry) })} />}</td>
            {columns.map(column => <td key={column.field}>{row.kind === "Item" && <InvoiceItemCellInput item={item} index={itemIndex} column={column} selected={selected} disabled={readOnly} onFocus={focus} onMouseDown={() => focus()} onChange={value => onChangeItem(itemIndex, { [column.field]: value })} />}</td>)}
          </tr>;
        })}</tbody>
      </table>
    </div>
    {!entries.length && <p>点击“新增商品”开始录入，再按需要插入说明或空白行。</p>}
    <div className="toolbar-actions"><button className="command-button secondary" type="button" disabled={!currentPage} onClick={() => changePage(currentPage - 1)}>上一页</button><span>{currentPage + 1} / {pages} · 共 {entries.length} 行</span><button className="command-button secondary" type="button" disabled={currentPage + 1 >= pages} onClick={() => changePage(currentPage + 1)}>下一页</button></div>
  </div>;
}
