import type { ApiInvoiceDetailDto } from "../../api/index.ts";
import { TextField } from "../../ui/FormFields.tsx";

type InvoicePatch = Partial<ApiInvoiceDetailDto>;

export function InvoiceExtendedFieldsPanel({
  invoice,
  isEditable,
  onChange,
}: {
  invoice: ApiInvoiceDetailDto;
  isEditable: boolean;
  onChange: (next: InvoicePatch) => void;
}) {
  return (
    <section className="form-section invoice-extended-fields-section" aria-label="报关与扩展字段">
      <div className="section-header">
        <h2>报关与扩展字段</h2>
      </div>
      <div className="field-grid">
        <TextField
          label="报关行名称"
          value={invoice.customsBrokerName ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ customsBrokerName: value })}
        />
        <TextField
          label="报关行编码"
          value={invoice.customsBrokerCode ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ customsBrokerCode: value })}
        />
        <TextField label="自定义备注 1" value={invoice.spare1 ?? ""} disabled={!isEditable} onChange={(value) => onChange({ spare1: value })} />
        <TextField label="自定义备注 2" value={invoice.spare2 ?? ""} disabled={!isEditable} onChange={(value) => onChange({ spare2: value })} />
        <TextField label="自定义备注 3" value={invoice.spare3 ?? ""} disabled={!isEditable} onChange={(value) => onChange({ spare3: value })} />
      </div>
    </section>
  );
}
