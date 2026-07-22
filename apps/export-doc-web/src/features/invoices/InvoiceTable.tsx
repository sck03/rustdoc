import type { KeyboardEvent } from "react";
import { Copy, Download, FileCheck2, FileSpreadsheet } from "lucide-react";
import type { ApiInvoiceListItemDto } from "../../api/index.ts";
import { formatAmount, formatDate } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { getInvoiceStatusLabel } from "./invoiceModel.ts";

export function InvoiceTable({ data, isBusy, canOperate, canExportBookingSheet, canUseSingleWindow, onOpen, onCopy, onExportPackage, onExportBookingSheet, onSingleWindow }: {
  data: ApiInvoiceListItemDto[]; isBusy: boolean; onOpen: (invoiceId: number) => void;
  canOperate: boolean; canExportBookingSheet: boolean; canUseSingleWindow: boolean;
  onCopy: (invoice: ApiInvoiceListItemDto) => void; onExportPackage: (invoice: ApiInvoiceListItemDto) => void;
  onExportBookingSheet: (invoice: ApiInvoiceListItemDto) => void; onSingleWindow: (invoice: ApiInvoiceListItemDto) => void;
}) {
  const openFromKeyboard = (event: KeyboardEvent<HTMLTableRowElement>, id: number) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); onOpen(id); } };
  const actions = [
    { title: "导出单据包", icon: Download, run: onExportPackage, visible: canOperate },
    { title: "导出货代订舱托单", icon: FileSpreadsheet, run: onExportBookingSheet, visible: canExportBookingSheet },
    { title: "单一窗口办理", icon: FileCheck2, run: onSingleWindow, visible: canUseSingleWindow },
    { title: "复制发票", icon: Copy, run: onCopy, visible: canOperate },
  ].filter((action) => action.visible);
  return <ResponsiveTableFrame label="发票列表" busy={isBusy} mobileLayout="scroll"><table><thead><tr>{["发票号","日期","客户","出口商","目的国","装港","目的港","金额","类型","状态","操作"].map((label) => <th key={label} className={label === "金额" ? "amount-cell" : undefined}>{label}</th>)}</tr></thead><tbody>
    {data.length === 0 ? <tr><td colSpan={11} className="empty-cell">{isBusy ? "加载中" : "暂无数据"}</td></tr> : data.map((invoice) => <tr className="clickable-row" key={invoice.id} tabIndex={0} onClick={() => onOpen(invoice.id)} onKeyDown={(event) => openFromKeyboard(event, invoice.id)}>
      <td className="strong-cell">{invoice.invoiceNo || "-"}</td><td>{formatDate(invoice.invoiceDate)}</td><td>{invoice.customerName || "-"}</td><td>{invoice.exporterName || "-"}</td><td>{invoice.destinationCountry || "-"}</td><td>{invoice.portOfLoading || "-"}</td><td>{invoice.portOfDestination || "-"}</td><td className="amount-cell">{formatAmount(invoice.totalAmount, invoice.currency)}</td><td><span className="status-pill">{invoice.type || "-"}</span></td><td><span className="status-pill">{getInvoiceStatusLabel(invoice.status)}</span></td>
      <td className="row-actions-cell">{actions.map(({ title, icon: Icon, run }) => <button key={title} className="icon-button compact-icon-button" type="button" title={title} aria-label={`${title} ${invoice.invoiceNo || invoice.id}`} disabled={isBusy} onClick={(event) => { event.stopPropagation(); run(invoice); }}><Icon size={15} aria-hidden="true" /></button>)}</td>
    </tr>)}
  </tbody></table></ResponsiveTableFrame>;
}
