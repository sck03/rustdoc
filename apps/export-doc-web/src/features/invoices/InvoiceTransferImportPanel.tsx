import type { FormEvent } from "react";
import { FileArchive, Upload, X } from "lucide-react";
import type { InvoiceTransferConflictAction, InvoiceTransferImportDraft } from "./invoiceListModels.ts";

export function InvoiceTransferImportPanel({ draft, isBusy, uploadMode = false, uploadFile, onUploadFileChange, onCancel, onChange, onPreview, onSubmit }: {
  draft: InvoiceTransferImportDraft; isBusy: boolean; onCancel: () => void;
  uploadMode?: boolean; uploadFile?: File | null; onUploadFileChange?: (file: File | null) => void;
  onChange: (next: Partial<InvoiceTransferImportDraft>) => void; onPreview: (packagePath: string) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
}) {
  const preview = draft.previewResponse?.preview ?? null;
  const hasConflict = Boolean(preview?.invoiceExists);
  return <section className="form-section" aria-label="导入发票单据包">
    <div className="section-header"><h2>导入单据包</h2><button className="icon-button" type="button" title="关闭导入" disabled={isBusy} onClick={onCancel}><X size={17} aria-hidden="true" /></button></div>
    <form className="settings-form" onSubmit={onSubmit}>
      <div className="field-grid">{uploadMode ? <label><span>单据包文件</span><input type="file" accept=".edpkg" disabled={isBusy} onChange={(event) => onUploadFileChange?.(event.target.files?.[0] ?? null)} /></label> : <label><span>单据包路径</span><input value={draft.packagePath} disabled={isBusy} onChange={(event) => onChange({ packagePath: event.target.value, previewResponse: null })} /></label>}</div>
      {!uploadMode ? <div className="toolbar-actions"><button className="command-button secondary" type="button" disabled={isBusy} onClick={() => onPreview(draft.packagePath)}><FileArchive size={17} aria-hidden="true" /><span>{isBusy ? "处理中" : "预览"}</span></button></div> : <div className="field-help">{uploadFile ? `已选择：${uploadFile.name}` : "选择文件后自动预览。"}</div>}
      {draft.previewResponse ? <><div className={draft.previewResponse.checksumValid ? "success-alert" : "alert"}>{draft.previewResponse.checksumMessage || (draft.previewResponse.checksumValid ? "校验通过" : "校验失败")}</div>
        {!draft.previewResponse.checksumValid ? <div className="inline-options"><label><input type="checkbox" checked={draft.allowInvalidChecksum} disabled={isBusy} onChange={(event) => onChange({ allowInvalidChecksum: event.target.checked })} /><span>继续导入校验失败的包</span></label></div> : null}
        {preview ? <div className="field-grid">
          <label><span>发票号</span><input value={preview.invoiceNo || "-"} disabled /></label><label><span>类型</span><input value={preview.type || "-"} disabled /></label>
          <label><span>商品条数</span><input value={preview.itemCount} disabled /></label><label><span>客户</span><input value={preview.customerExists ? "已存在" : "将新建/匹配"} disabled /></label>
          <label><span>出口商</span><input value={preview.exporterExists ? "已存在" : "将新建/匹配"} disabled /></label><label><span>同号同类型</span><input value={preview.invoiceExists ? (preview.invoiceMatches ? "完全相同" : "存在差异") : "不存在"} disabled /></label>
        </div> : null}</> : null}
      {hasConflict ? <div className="field-grid"><label><span>冲突处理</span><select value={draft.conflictAction} disabled={isBusy} onChange={(event) => onChange({ conflictAction: event.target.value as InvoiceTransferConflictAction })}>
        <option value="NewInvoiceNo">另存新发票号</option><option value="Overwrite">覆盖同号同类型</option><option value="AppendItems">追加商品明细</option><option value="Skip">跳过</option>
      </select></label>{draft.conflictAction === "NewInvoiceNo" ? <label><span>新发票号</span><input value={draft.newInvoiceNo} disabled={isBusy} onChange={(event) => onChange({ newInvoiceNo: event.target.value })} /></label> : null}</div> : null}
      <div className="toolbar-actions"><button className="command-button secondary" type="button" disabled={isBusy} onClick={onCancel}>取消</button><button className="command-button" type="submit" disabled={!draft.previewResponse || isBusy}><Upload size={17} aria-hidden="true" /><span>{isBusy ? "导入中" : "导入"}</span></button></div>
    </form>
  </section>;
}
