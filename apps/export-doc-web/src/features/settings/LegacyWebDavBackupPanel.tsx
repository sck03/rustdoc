import type { BackupManagementController } from "./useBackupManagement.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { formatBytes } from "./settingsFormatters.ts";

export function LegacyWebDavBackupPanel({ controller: c }: { controller: BackupManagementController }) {
  return <>
    <p className="settings-helper-text">此入口保留已有 WebDAV 数据库快照的读取。新的自动副本请使用“异机副本”，其中保存完整加密包。</p>
    <div className="backup-action-grid">
      <SelectField label="历史云端快照" value={c.cloudDownloadFileName} disabled={!c.cloudBackupsEnabled || c.isBusy || !c.cloudBackups.length}
        options={c.cloudBackups.map((backup) => ({ value: backup.fileName, label: `${backup.fileName} (${formatBytes(backup.sizeBytes)})` }))} onChange={c.setCloudDownloadFileName} />
      <button className="command-button secondary" type="button" disabled={!c.canDownloadCloud} onClick={c.downloadCloudBackup}>下载并校验历史快照</button>
      <button className="command-button secondary" type="button" disabled={!c.canUploadCloud} onClick={c.uploadLatestBackup}>上传最新数据库快照</button>
    </div>
  </>;
}
