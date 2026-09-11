import { X } from "lucide-react";
import type { ApiInvoiceListItemDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { RemoteSelectField } from "../../ui/RemoteSelectField.tsx";

export function InvoiceReportSelection({ client, selected, disabled, onChange }: {
  client: ExportDocManagerApiClient;
  selected: ApiInvoiceListItemDto[];
  disabled: boolean;
  onChange: (invoices: ApiInvoiceListItemDto[]) => void;
}) {
  return <div className="job-invoice-selection">
    <RemoteSelectField<ApiInvoiceListItemDto> label="选择发票" value="" disabled={disabled || selected.length >= 200}
      queryKey={[...queryKeys.invoicesRoot(), "report-selection"]}
      searchPlaceholder="按发票号或客户搜索" emptyLabel="选择发票后加入清单"
      description="可连续添加多张发票，单次最多 200 张；已选发票显示在下方。"
      loadOptions={async (keyword, signal) => (await client.listInvoices({ pageNumber: 1, pageSize: 20, keyword }, { signal })).items}
      getValue={(invoice) => String(invoice.id)} getLabel={(invoice) => `${invoice.invoiceNo} · ${invoice.customerName}`}
      onChange={(invoice) => { if (invoice && !selected.some((item) => item.id === invoice.id)) onChange([...selected, invoice]); }} />
    {selected.length > 0 && <ul className="job-invoice-selection-list" aria-label="已选发票">
      {selected.map((invoice) => <li key={invoice.id}>
        <span><strong>{invoice.invoiceNo}</strong><small>{invoice.customerName}</small></span>
        <button className="icon-button" type="button" aria-label={`移除发票 ${invoice.invoiceNo}`} disabled={disabled}
          onClick={() => onChange(selected.filter((item) => item.id !== invoice.id))}><X size={16} aria-hidden="true" /></button>
      </li>)}
    </ul>}
  </div>;
}
