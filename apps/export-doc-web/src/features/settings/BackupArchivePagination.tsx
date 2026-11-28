import type { BackupPlanController } from "./useBackupPlan.ts";

export function BackupArchivePagination({ controller: c, disabled }: { controller: BackupPlanController; disabled: boolean }) {
  const data = c.archiveQuery.data;
  return <nav className="toolbar-actions" aria-label="备份分页">
    <button className="command-button secondary" type="button" disabled={disabled || c.archiveQuery.isFetching || c.archivePage <= 1} onClick={() => c.setArchivePage(c.archivePage - 1)}>上一页</button>
    <span>第 {c.archivePage} 页 · 共 {data?.totalCount ?? 0} 份</span>
    <button className="command-button secondary" type="button" disabled={disabled || c.archiveQuery.isFetching || !data || c.archivePage * data.pageSize >= data.totalCount} onClick={() => c.setArchivePage(c.archivePage + 1)}>下一页</button>
    <button className="command-button secondary" type="button" disabled={disabled || c.archiveQuery.isFetching} onClick={() => void c.archiveQuery.refetch()}>刷新</button>
  </nav>;
}
