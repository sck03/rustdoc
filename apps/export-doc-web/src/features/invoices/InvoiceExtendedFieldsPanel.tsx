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
    <section className="form-section invoice-extended-fields-section" aria-label="报关行信息">
      <div className="section-header">
        <h2>报关行信息</h2>
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
      </div>
    </section>
  );
}
