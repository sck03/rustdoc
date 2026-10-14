import { Download, RotateCcw } from "lucide-react";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import type { BackupPlanController } from "./useBackupPlan.ts";
import { replicaResultLabel } from "./backupPlanModel.ts";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";

export function BackupArchivesPanel({ controller: c, disabled, onRestore }: { controller: BackupPlanController; disabled: boolean; onRestore: (id: string) => void }) {
  return <section className="form-section" aria-label="最近完整备份">
    <div className="section-header"><h3>最近完整备份</h3><span>最多显示最近 20 份</span></div>
    <ResponsiveTableFrame className="backup-table-frame" label="完整备份列表"><table className="backup-table" aria-label="完整备份列表">
      <thead><tr><th>创建时间</th><th>大小</th><th>异机副本</th><th>操作</th></tr></thead>
      <tbody>{c.status?.archives.length ? c.status.archives.map((archive) => <tr key={archive.id}>
        <td title={archive.fileName}>{formatRuntimeDate(archive.createdAt)}</td><td>{formatBytes(archive.sizeBytes)}</td>
        <td title={archive.message}>{replicaResultLabel(archive.replicaStatus)}{archive.message && <small className="settings-helper-text">{archive.message}</small>}</td>
        <td><div className="toolbar-actions"><button className="command-button secondary" type="button" disabled={disabled} onClick={() => c.download(archive.id, archive.fileName)}><Download size={16} aria-hidden="true" /><span>下载</span></button><button className="command-button secondary" type="button" disabled={disabled} onClick={() => onRestore(archive.id)}><RotateCcw size={16} aria-hidden="true" /><span>用于恢复</span></button></div></td>
      </tr>) : <tr><td colSpan={4} className="empty-cell">尚无计划备份，可在“日常灾备”保存设置后立即备份一次。</td></tr>}</tbody>
    </table></ResponsiveTableFrame>
  </section>;
}
