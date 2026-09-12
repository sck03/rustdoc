import { useId, useState, type ReactNode } from "react";
import { X } from "lucide-react";
import { useModalDialog } from "../../ui/useModalDialog.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import type { OfficePage } from "./officeModel.ts";
import type { useOfficePaging } from "./useOfficeData.ts";

export function OfficeDialog({ title, children, onClose, busy = false, error = "", protectChanges = false, hasChanges = false }: {
  title: string; children: ReactNode; onClose: () => void; busy?: boolean; error?: string; protectChanges?: boolean; hasChanges?: boolean;
}) {
  const titleId = useId();
  const [dirty, setDirty] = useState(false);
  const requestConfirmation = useConfirmation();
  function close() {
    if (busy) return;
    if (!protectChanges || !dirty && !hasChanges) { onClose(); return; }
    void requestConfirmation({ title: "放弃未提交的内容？", description: "当前填写内容尚未提交。", confirmLabel: "放弃并关闭" })
      .then((accepted) => { if (accepted) onClose(); });
  }
  const ref = useModalDialog(close, { canClose: !busy });
  return <div className="office-dialog-backdrop">
    <div className="office-dialog" ref={ref} role="dialog" aria-modal="true" aria-labelledby={titleId}>
      <header className="office-dialog-header"><h2 id={titleId}>{title}</h2>
        <button type="button" className="icon-button" aria-label="关闭窗口" disabled={busy} onClick={close}><X size={18} aria-hidden="true" /></button>
      </header>
      <div className="office-dialog-body" onChangeCapture={() => setDirty(true)}>
        {error && <InlineNotice tone="error" title="操作未完成">{error}</InlineNotice>}
        {children}
      </div>
    </div>
  </div>;
}

export function OfficeField({ label, children, wide = false }: { label: string; children: ReactNode; wide?: boolean }) {
  return <label className={wide ? "office-field office-field-wide" : "office-field"}><span>{label}</span>{children}</label>;
}

export function OfficeSubmit({ busy, disabled = false, label = "保存" }: { busy: boolean; disabled?: boolean; label?: string }) {
  return <footer className="office-form-actions"><button className="command-button" type="submit" disabled={busy || disabled}>{busy ? "正在处理…" : label}</button></footer>;
}

export function OfficeQueryState({ query, emptyTitle }: { query: { isPending: boolean; isError: boolean; error: unknown; data?: { items: unknown[] }; refetch: () => unknown }; emptyTitle: string }) {
  if (query.isPending) return <PageState tone="loading" title="正在加载" />;
  if (query.isError) return <PageState tone="error" title="加载失败" description={readApiError(query.error)}
    action={<button className="command-button secondary" type="button" onClick={() => void query.refetch()}>重新加载</button>} />;
  return query.data?.items.length ? null : <PageState title={emptyTitle} />;
}

export function OfficePager({ page, paging, busy }: {
  page?: OfficePage<unknown>; paging: { pageNumber: number; pageSize: number; setPageNumber: (page: number) => void; changePageSize: (size: number) => void }; busy: boolean;
}) {
  return <ListPaginationControls pageNumber={page?.pageNumber ?? paging.pageNumber} totalPages={page?.totalPages ?? 1}
    totalCount={page?.totalCount ?? 0} pageSize={paging.pageSize} pageSizeOptions={[12, 24, 48, 96]} isBusy={busy}
    onPageChange={paging.setPageNumber} onPageSizeChange={paging.changePageSize} />;
}

export function OfficeTabs({ records, onChange, recordsLabel, resourcesLabel = "资源与申请" }: {
  records: boolean; onChange: (records: boolean) => void; recordsLabel: string; resourcesLabel?: string;
}) {
  return <div className="office-tabs" role="group" aria-label="切换行政工作区">
    <button type="button" aria-pressed={!records} onClick={() => onChange(false)}>{resourcesLabel}</button>
    <button type="button" aria-pressed={records} onClick={() => onChange(true)}>{recordsLabel}</button>
  </div>;
}
