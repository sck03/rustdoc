import { Archive, RefreshCw, RotateCcw, Trash2 } from "lucide-react";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { NumberField, SelectField } from "../../ui/FormFields.tsx";
import { formatManagedPath } from "./settingsFormatters.ts";
import type { BackupManagementController } from "./useBackupManagement.ts";

export function DatabaseBackupPanel({ controller: c, onPathError }: { controller: BackupManagementController; onPathError: (message: string) => void }) {
  return <>
    <div className="section-header"><h3>数据库快照</h3><div className="toolbar-actions">
      <button className="command-button secondary" type="button" disabled={!c.canManageSettings || c.isBusy} onClick={c.refreshBackups}><RefreshCw size={17} aria-hidden="true" /><span>刷新备份</span></button>
      <button className="command-button" type="button" disabled={!c.canManageSettings || c.isBusy} onClick={c.createBackup}><Archive size={17} aria-hidden="true" /><span>创建数据库快照</span></button>
    </div></div>
    <p className="settings-helper-text">用于当前安装内的数据回退。更换机器请使用完整加密包，以便同时迁移主密钥和用户文件模板。</p>
    <div className="detail-value-row"><span>备份目录</span><strong title={formatManagedPath(c.backupRoot)}>{formatManagedPath(c.backupRoot)}</strong>{renderOpenPathAction(c.backupRoot, "打开备份目录", onPathError)}</div>
    <details className="maintenance-advanced-details"><summary>恢复数据库或清理旧快照</summary>
      <div className="backup-action-grid">
        <SelectField label="还原备份" value={c.restoreFileName} disabled={!c.canManageSettings || c.isBusy || !c.backups.length} options={c.backups.map((backup) => ({ value: backup.fileName, label: backup.fileName }))} onChange={(value) => { c.setRestoreFileName(value); c.setRestoreConfirmation(""); }} />
        <label><span>确认文本</span><input value={c.restoreConfirmation} disabled={!c.canManageSettings || c.isBusy} placeholder="RESTORE" onChange={(event) => c.setRestoreConfirmation(event.target.value)} /></label>
        <button className="command-button danger-command" type="button" disabled={!c.canRestore} onClick={c.restoreBackup}><RotateCcw size={17} aria-hidden="true" /><span>还原数据库</span></button>
      </div>
      <div className="backup-action-grid">
        <NumberField label="快照保留天数" value={c.cleanupDays} disabled={!c.canManageSettings || c.isBusy} step="1" onChange={c.setCleanupDays} />
        <button className="command-button secondary" type="button" disabled={!c.canManageSettings || c.isBusy} onClick={c.cleanupBackups}><Trash2 size={17} aria-hidden="true" /><span>清理旧快照</span></button>
      </div>
    </details>
  </>;
}
