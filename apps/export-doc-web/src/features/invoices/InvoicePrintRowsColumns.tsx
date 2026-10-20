import { useState } from "react";
import { documentFieldLabel, useDocumentFieldLabels } from "../../ui/DocumentFieldLabelsContext.tsx";
import { invoiceItemEditableColumns, type EditableInvoiceItemField } from "./invoiceItemTableModel.ts";

const defaults: EditableInvoiceItemField[] = ["cartons", "ctnUnitEN", "quantity", "unitEN", "unitPrice", "totalPrice"];

export function usePrintRowColumns() {
  const [fields, setFields] = useState(defaults);
  const labels = useDocumentFieldLabels("item");
  const columns = fields.map(field => invoiceItemEditableColumns.find(column => column.field === field)!);
  return {
    columns,
    menu: <details className="item-column-visibility-menu">
      <summary className="command-button secondary">录入显示列</summary>
      <div className="item-column-menu" role="group" aria-label="打印排版录入列">
        <div className="item-column-menu-header"><span>显示 {fields.length} 列</span><button type="button" onClick={() => setFields(defaults)}>恢复常用列</button></div>
        <p className="item-column-default-hint">这里选择录入时显示的商品资料。打印列及其顺序、宽度在模板中设置。</p>
        <div className="item-column-menu-list">{invoiceItemEditableColumns.map(column => <label className="item-column-option" key={column.field}>
          <input type="checkbox" checked={fields.includes(column.field)} onChange={event => setFields(current => event.target.checked ? [...current, column.field] : current.filter(field => field !== column.field))} />
          <span>{documentFieldLabel(labels, column.field, column.header)}</span>
        </label>)}</div>
      </div>
    </details>,
    label: (field: EditableInvoiceItemField, fallback: string) => documentFieldLabel(labels, field, fallback),
  };
}
