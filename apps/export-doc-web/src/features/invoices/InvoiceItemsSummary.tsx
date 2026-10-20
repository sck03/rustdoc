import type { ApiInvoiceDetailDto } from "../../api/index.ts";
import { formatAmount, formatPlainNumber } from "../../ui/formUtils.ts";
import { invoiceItemVolumeDisplayValue, invoiceItemWeightDisplayValue } from "./invoiceItemsEditorModel.ts";

export function InvoiceItemsSummary({ count, totals, currency }: { count: number; totals: Partial<ApiInvoiceDetailDto>; currency?: string }) {
  return <div className="item-summary-bar">
    <span>{count} 行</span>
    <span>数量 {formatPlainNumber(totals.totalQuantity ?? 0)}</span>
    <span>箱数 {formatPlainNumber(totals.totalCartons ?? 0)}</span>
    <span>毛重 {invoiceItemWeightDisplayValue(totals.totalGrossWeight ?? 0)}</span>
    <span>体积 {invoiceItemVolumeDisplayValue(totals.totalVolume ?? 0)}</span>
    <span>金额合计 {formatAmount(totals.totalAmount ?? 0, currency)}</span>
  </div>;
}
