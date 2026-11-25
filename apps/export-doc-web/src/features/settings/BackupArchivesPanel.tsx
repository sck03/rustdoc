import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { Download, RotateCcw } from "lucide-react";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import type { BackupPlanController } from "./useBackupPlan.ts";
import { replicaResultLabel } from "./backupPlanModel.ts";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";

export function BackupArchivesPanel({ controller: c, disabled, onRestore }: { controller: BackupPlanController; disabled: boolean; onRestore: (id: string) => void }) {
  return <section className="form-section" aria-label="完整备份">
    <div className="section-header"><h3>完整备份</h3></div>
    {c.archiveQuery.isError && <InlineNotice tone="error">{readApiError(c.archiveQuery.error)}</InlineNotice>}
    <ResponsiveTableFrame className="backup-table-frame" label="完整备份列表"><table className="backup-table" aria-label="完整备份列表">
      <thead><tr><th>创建时间</th><th>大小</th><th>异机副本</th><th>操作</th></tr></thead>
      <tbody>{c.archives.length ? c.archives.map((archive) => <tr key={archive.id}>
        <td title={archive.fileName}>{formatRuntimeDate(archive.createdAt)}</td><td>{formatBytes(archive.sizeBytes)}</td>
        <td title={archive.message}>{!archive.available && <span role="status">{archive.integrityMessage}</span>}{replicaResultLabel(archive.replicaStatus)}{archive.message && <small className="settings-helper-text">{archive.message}</small>}</td>
        <td><div className="toolbar-actions"><button className="command-button secondary" type="button" disabled={disabled || !archive.available} onClick={() => c.download(archive.id, archive.fileName)}><Download size={16} aria-hidden="true" /><span>下载</span></button><button className="command-button secondary" type="button" disabled={disabled || !archive.available} onClick={() => onRestore(archive.id)}><RotateCcw size={16} aria-hidden="true" /><span>用于恢复</span></button></div></td>
      </tr>) : <tr><td colSpan={4} className="empty-cell">{c.archiveQuery.isPending ? "正在加载备份…" : c.archiveQuery.isError ? "备份列表加载失败。" : "尚无计划备份。"}</td></tr>}</tbody>
    </table></ResponsiveTableFrame>
    <nav className="toolbar-actions" aria-label="备份分页">
      <button className="command-button secondary" type="button" disabled={disabled || c.archiveQuery.isFetching || c.archivePage <= 1} onClick={() => c.setArchivePage(c.archivePage - 1)}>上一页</button>
      <span>第 {c.archivePage} 页 · 共 {c.archiveQuery.data?.totalCount ?? 0} 份</span>
      <button className="command-button secondary" type="button" disabled={disabled || c.archiveQuery.isFetching || !c.archiveQuery.data || c.archivePage * 20 >= c.archiveQuery.data.totalCount} onClick={() => c.setArchivePage(c.archivePage + 1)}>下一页</button>
      <button className="command-button secondary" type="button" disabled={c.archiveQuery.isFetching} onClick={() => void c.archiveQuery.refetch()}>刷新</button>
    </nav>
  </section>;
}
