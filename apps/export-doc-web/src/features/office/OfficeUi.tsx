import type { ReactNode } from "react";
import { PageState } from "../../ui/PageState.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { SegmentedNav } from "../../ui/primitives/Tabs.tsx";
import type { OfficePage } from "./officeModel.ts";
export { Dialog as OfficeDialog } from "../../ui/primitives/Dialog.tsx";
export { Field as OfficeField } from "../../ui/primitives/Field.tsx";

export function OfficeSubmit({ busy, disabled = false, label = "保存", children }: { busy: boolean; disabled?: boolean; label?: string; children?: ReactNode }) {
  return <footer className="office-form-actions">{children}<button className="command-button" type="submit" disabled={busy || disabled}>{busy ? "正在处理…" : label}</button></footer>;
}
export function OfficeQueryState({ query, emptyTitle }: { query: { isPending: boolean; isError: boolean; error: unknown; data?: { items: unknown[] }; refetch: () => unknown }; emptyTitle: string }) {
  if (query.isPending) return <PageState tone="loading" title="正在加载" />;
  if (query.isError) return <PageState tone="error" title="加载失败" description={readApiError(query.error)} action={<button className="command-button secondary" type="button" onClick={() => void query.refetch()}>重新加载</button>} />;
  return query.data?.items.length ? null : <PageState title={emptyTitle} />;
}
export function OfficePager({ page, paging, busy }: {
  page?: OfficePage<unknown>; paging: { pageNumber: number; pageSize: number; setPageNumber: (page: number) => void; changePageSize: (size: number) => void }; busy: boolean;
}) {
  return <Pagination pageNumber={page?.pageNumber ?? paging.pageNumber} totalPages={page?.totalPages ?? 1} totalCount={page?.totalCount ?? 0}
    pageSize={paging.pageSize} pageSizeOptions={[12,24,48,96]} isBusy={busy} onPageChange={paging.setPageNumber} onPageSizeChange={paging.changePageSize} />;
}
export function OfficeTabs({ records, onChange, recordsLabel, resourcesLabel = "资源与申请" }: { records: boolean; onChange: (records: boolean) => void; recordsLabel: string; resourcesLabel?: string }) {
  return <SegmentedNav label="切换行政工作区" value={records ? "records" : "resources"} onChange={value => onChange(value === "records")}
    items={[{key:"resources",label:resourcesLabel},{key:"records",label:recordsLabel}]} />;
}
