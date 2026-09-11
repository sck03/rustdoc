import { useEffect, useMemo, useState } from "react";
import type { ApiInvoiceItemDto } from "../../api/index.ts";
import { invoiceItemEditableColumns, type EditableInvoiceItemField } from "./invoiceItemTableModel.ts";
import { findPopulatedInvoiceSpareColumns, resolveInvoiceItemHiddenColumns, type InvoiceItemColumnOverrides } from "./invoiceItemColumnVisibility.ts";

export function useInvoiceItemColumnVisibility(items: ApiInvoiceItemDto[], defaultSpareColumnCount: number) {
  const populated = useMemo(() => findPopulatedInvoiceSpareColumns(items), [items]);
  const [revealed, setRevealed] = useState(populated);
  const [overrides, setOverrides] = useState<InvoiceItemColumnOverrides>({});
  useEffect(() => {
    // Keep a revealed column available while its last value is being cleared.
    setRevealed((current) => populated.every((field) => current.includes(field)) ? current : [...new Set([...current, ...populated])]);
  }, [populated]);
  const hiddenColumnFields = useMemo(() => resolveInvoiceItemHiddenColumns(defaultSpareColumnCount, [...revealed, ...populated], overrides),
    [defaultSpareColumnCount, overrides, populated, revealed]);
  return {
    hiddenColumnFields,
    setColumnVisible: (field: EditableInvoiceItemField, visible: boolean) => setOverrides((current) => ({ ...current, [field]: visible })),
    showAllColumns: () => setOverrides(Object.fromEntries(invoiceItemEditableColumns.map((column) => [column.field, true]))),
    resetColumns: () => { setOverrides({}); setRevealed(populated); },
  };
}
