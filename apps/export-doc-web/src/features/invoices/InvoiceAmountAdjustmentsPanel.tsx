import type { ApiInvoiceDetailDto } from "../../api/index.ts";
import { sumDecimalStrings } from "../../ui/decimalSum.ts";

export function InvoiceAmountAdjustmentsPanel({ invoice, editable, onChange }: {
  invoice: ApiInvoiceDetailDto;
  editable: boolean;
  onChange: (patch: Partial<ApiInvoiceDetailDto>) => void;
}) {
  const rows = invoice.amountAdjustments ?? [];
  let total = "请完善调整金额";
  try {
    const result = sumDecimalStrings([String(invoice.totalAmount), ...rows.map(row => row.amount.trim() || "0")]);
    const [whole, fraction = ""] = result.split(".");
    total = `${invoice.currency} ${whole}.${fraction.padEnd(2, "0")}`;
  } catch { /* Incomplete input remains editable and cannot be saved as a valid adjustment. */ }
  return <details className="invoice-new-optional-section information-tier-advanced" onInvalidCapture={event => { event.currentTarget.open = true; }}>
    <summary><span>金额调整（定金、样品费等）</span><small>{rows.length ? `${rows.length} 行调整` : "通常无需填写，按需展开"}</small></summary>
    <p className="field-help">同发票币种：定金填负数，例如 -10963.50；样品费或其他费用填正数。支持多行说明，仅影响报表应付合计，商品金额与利润口径保持不变。</p>
    {rows.map((row, index) => <fieldset key={index} className="form-grid" disabled={!editable}>
      <legend>调整 {index + 1}</legend>
      <label><span>说明</span><textarea rows={2} maxLength={200} required={Boolean(row.amount.trim() && !/^[+-]?0+(?:\.0{1,2})?$/.test(row.amount.trim()))} value={row.description} placeholder="RECEIVED DEPOSIT / SAMPLE CHARGE"
        onChange={event => onChange({ amountAdjustments: rows.map((current, position) => position === index ? { ...current, description: event.target.value } : current) })} /></label>
      <label><span>增减金额（{invoice.currency}）</span><input inputMode="decimal" maxLength={32} required={Boolean(row.description.trim())} pattern="[+\-]?[0-9]+([.][0-9]{1,2})?" value={row.amount} placeholder="负数扣减，正数增加"
        onChange={event => onChange({ amountAdjustments: rows.map((current, position) => position === index ? { ...current, amount: event.target.value } : current) })} /></label>
      <button type="button" className="command-button secondary" onClick={() => onChange({ amountAdjustments: rows.filter((_, position) => position !== index) })}>删除调整 {index + 1}</button>
    </fieldset>)}
    <button type="button" className="command-button secondary" disabled={!editable || rows.length >= 32} onClick={() => onChange({ amountAdjustments: [...rows, { description: "", amount: "" }] })}>添加金额调整</button>
    {rows.length > 0 && <p><strong>调整后应付金额：{total}</strong></p>}
  </details>;
}
