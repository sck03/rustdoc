import { Columns3 } from "lucide-react";
import { invoiceItemEditableColumns, type EditableInvoiceItemField } from "./invoiceItemTableModel.ts";

export function InvoiceItemColumnMenu({ hiddenColumnFields, defaultSpareColumnCount, onShowAll, onReset, onToggle }: {
  hiddenColumnFields: Set<EditableInvoiceItemField>;
  defaultSpareColumnCount: number;
  onShowAll: () => void;
  onReset: () => void;
  onToggle: (field: EditableInvoiceItemField) => void;
}) {
  const visibleCount = invoiceItemEditableColumns.length - hiddenColumnFields.size;
  return <details className="item-column-visibility-menu">
    <summary className="command-button secondary" title="显示/隐藏明细列" aria-label="显示/隐藏明细列">
      <Columns3 size={16} aria-hidden="true" /><span>显示列</span>
    </summary>
    <div className="item-column-menu" role="group" aria-label="明细显示列">
      <div className="item-column-menu-header">
        <span>显示 {visibleCount} 列</span>
        <button type="button" className="text-button compact-text-button" onClick={onReset}>恢复默认</button>
        <button type="button" className="text-button compact-text-button" onClick={onShowAll}>全部显示</button>
      </div>
      <p className="item-column-default-hint">{defaultSpareColumnCount ? `默认显示前 ${defaultSpareColumnCount} 个备用列` : "空备用列默认隐藏"}，已有内容的列自动显示。默认数量可在“系统设置 → 运行与数据库 → 发票录入默认值”调整。</p>
      <div className="item-column-menu-list">
        {invoiceItemEditableColumns.map((column) => {
          const checked = !hiddenColumnFields.has(column.field);
          return <label className="item-column-option" key={column.field}>
            <input type="checkbox" checked={checked} disabled={checked && visibleCount <= 1} onChange={() => onToggle(column.field)} />
            <span>{column.header}</span>
          </label>;
        })}
      </div>
    </div>
  </details>;
}
