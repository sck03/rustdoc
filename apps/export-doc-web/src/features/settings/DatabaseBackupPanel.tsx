import { Archive, CloudDownload, CloudUpload, RefreshCw, RotateCcw, Trash2 } from "lucide-react";
import type { ApiCloudBackupStatusResponse } from "../../api/index.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { NumberField, SelectField } from "../../ui/FormFields.tsx";
import { formatBytes, formatManagedPath } from "./settingsFormatters.ts";
import type { BackupManagementController } from "./useBackupManagement.ts";

export function DatabaseBackupPanel({
  controller,
  onPathError,
}: {
  controller: BackupManagementController;
  onPathError: (message: string) => void;
}) {
  return (
    <>
      <div className="section-header">
        <h2>数据备份与还原</h2>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新备份" aria-label="刷新备份" disabled={!controller.canManageSettings || controller.isBusy} onClick={controller.refreshBackups}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="command-button" type="button" disabled={!controller.canManageSettings || controller.isBusy} onClick={controller.createBackup}>
            <Archive size={17} aria-hidden="true" />
            <span>创建备份</span>
          </button>
          <button className="command-button secondary" type="button" disabled={!controller.canUploadCloud} onClick={controller.uploadLatestBackup}>
            <CloudUpload size={17} aria-hidden="true" />
            <span>上传最新备份</span>
          </button>
          <button className="command-button secondary" type="button" disabled={!controller.canDownloadCloud} onClick={controller.downloadCloudBackup}>
            <CloudDownload size={17} aria-hidden="true" />
            <span>下载云备份</span>
          </button>
        </div>
      </div>
      <div className="detail-grid runtime-detail-grid">
        <div className="detail-item detail-item-wide">
          <span>备份目录</span>
          <div className="detail-value-row">
            <strong title={formatManagedPath(controller.backupRoot)}>{formatManagedPath(controller.backupRoot)}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(controller.backupRoot, "打开备份目录", onPathError)}</div>
          </div>
        </div>
        <CloudBackupStatusDetail status={controller.cloudStatus} />
      </div>
      <div className="backup-action-grid">
        <NumberField label="保留天数" value={controller.cleanupDays} disabled={!controller.canManageSettings || controller.isBusy} step="1" onChange={controller.setCleanupDays} />
        <button className="command-button secondary" type="button" disabled={!controller.canManageSettings || controller.isBusy} onClick={controller.cleanupBackups}>
          <Trash2 size={17} aria-hidden="true" />
          <span>清理旧备份</span>
        </button>
        <SelectField
          label="云端备份"
          value={controller.cloudDownloadFileName}
          disabled={!controller.cloudBackupsEnabled || controller.isBusy || controller.cloudBackups.length === 0}
          options={controller.cloudBackups.map((backup) => ({ value: backup.fileName, label: `${backup.fileName} (${formatBytes(backup.sizeBytes)})` }))}
          onChange={controller.setCloudDownloadFileName}
        />
        <SelectField
          label="还原备份"
          value={controller.restoreFileName}
          disabled={!controller.canManageSettings || controller.isBusy || controller.backups.length === 0}
          options={controller.backups.map((backup) => ({ value: backup.fileName, label: backup.fileName }))}
          onChange={(value) => {
            controller.setRestoreFileName(value);
            controller.setRestoreConfirmation("");
          }}
        />
        <label>
          <span>确认文本</span>
          <input
            value={controller.restoreConfirmation}
            disabled={!controller.canManageSettings || controller.isBusy || controller.backups.length === 0}
            placeholder="RESTORE"
            onChange={(event) => controller.setRestoreConfirmation(event.target.value)}
          />
        </label>
        <button className="command-button danger-command" type="button" disabled={!controller.canRestore} onClick={controller.restoreBackup}>
          <RotateCcw size={17} aria-hidden="true" />
          <span>还原数据库</span>
        </button>
      </div>
    </>
  );
}

function CloudBackupStatusDetail({ status }: { status: ApiCloudBackupStatusResponse | null }) {
  const stateText = status ? `${status.enabled ? "已启用" : "未启用"} · ${status.isConfigured ? "已配置" : "未配置"}` : "加载中";
  const latestText = status?.latestBackupFileName ? `${status.latestBackupFileName} (${formatBytes(status.latestBackupSizeBytes)})` : "暂无本地备份";

  return (
    <>
      <div className="detail-item">
        <span>WebDAV 云备份</span>
        <strong title={status?.url || stateText}>{stateText}</strong>
      </div>
      <div className="detail-item detail-item-wide">
        <span>最新本地备份</span>
        <strong title={latestText}>{latestText}</strong>
      </div>
    </>
  );
}
