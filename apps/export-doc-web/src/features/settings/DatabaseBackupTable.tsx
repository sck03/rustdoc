import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { formatBytes, formatManagedPath, formatRuntimeDate, serverManagedFileLabel } from "./settingsFormatters.ts";
import type { BackupManagementController } from "./useBackupManagement.ts";

export function DatabaseBackupTable({
  controller,
  onPathError,
}: {
  controller: BackupManagementController;
  onPathError: (message: string) => void;
}) {
  return (
    <ResponsiveTableFrame className="backup-table-frame" label="数据库备份列表">
      <table className="backup-table" aria-label="数据库备份列表">
        <thead>
          <tr>
            <th>文件</th>
            <th>大小</th>
            <th>创建时间</th>
            <th>更新时间</th>
            <th>路径</th>
          </tr>
        </thead>
        <tbody>
          {controller.backups.length > 0 ? (
            controller.backups.map((backup) => (
              <tr key={backup.fullPath || backup.fileName}>
                <td>{backup.fileName}</td>
                <td>{formatBytes(backup.sizeBytes)}</td>
                <td>{formatRuntimeDate(backup.createdAt)}</td>
                <td>{formatRuntimeDate(backup.lastWriteTime)}</td>
                <td>
                  <div className="table-path-cell">
                    <span title={formatManagedPath(backup.fullPath, serverManagedFileLabel)}>{formatManagedPath(backup.fullPath, serverManagedFileLabel)}</span>
                    {renderOpenPathAction(backup.fullPath, "打开备份文件", onPathError)}
                  </div>
                </td>
              </tr>
            ))
          ) : (
            <tr>
              <td className="empty-cell" colSpan={5}>
                {controller.canManageSettings ? (controller.backupLoading ? "加载中" : "暂无备份") : "无权限"}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </ResponsiveTableFrame>
  );
}
