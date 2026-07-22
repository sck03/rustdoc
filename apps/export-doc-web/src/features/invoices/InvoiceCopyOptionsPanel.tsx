import type { FormEvent } from "react";
import { Copy, X } from "lucide-react";
import type { InvoiceCopyDraft } from "./invoiceListModels.ts";

export function InvoiceCopyOptionsPanel({ draft, isBusy, onCancel, onChange, onSubmit }: {
  draft: InvoiceCopyDraft;
  isBusy: boolean;
  onCancel: () => void;
  onChange: (next: Partial<InvoiceCopyDraft>) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
}) {
  const options: Array<{ key: keyof Pick<InvoiceCopyDraft, "copyHeader" | "copyItems" | "resetStatus" | "resetDates" | "clearAmounts">; label: string }> = [
    { key: "copyHeader", label: "复制票头信息" }, { key: "copyItems", label: "复制商品明细" },
    { key: "resetStatus", label: "状态重置为草稿" }, { key: "resetDates", label: "日期重置为今天" },
    { key: "clearAmounts", label: "清空金额" },
  ];
  return <section className="form-section" aria-label="复制发票选项">
    <div className="section-header"><h2>复制发票</h2><button className="icon-button" type="button" title="取消复制" aria-label="取消复制" disabled={isBusy} onClick={onCancel}><X size={17} aria-hidden="true" /></button></div>
    <form className="settings-form" onSubmit={onSubmit}>
      <div className="field-grid">
        <label><span>来源发票</span><input value={draft.source.invoiceNo || "-"} disabled /></label>
        <label><span>新发票号</span><input value={draft.newInvoiceNo} required disabled={isBusy} onChange={(event) => onChange({ newInvoiceNo: event.target.value })} /></label>
      </div>
      <div className="inline-options" aria-label="复制选项">{options.map((option) => <label key={option.key}><input type="checkbox" checked={draft[option.key]} disabled={isBusy} onChange={(event) => onChange({ [option.key]: event.target.checked })} /><span>{option.label}</span></label>)}</div>
      <div className="toolbar-actions"><button className="command-button secondary" type="button" disabled={isBusy} onClick={onCancel}>取消</button><button className="command-button" type="submit" disabled={isBusy}><Copy size={17} aria-hidden="true" /><span>{isBusy ? "复制中" : "开始复制"}</span></button></div>
    </form>
  </section>;
}
