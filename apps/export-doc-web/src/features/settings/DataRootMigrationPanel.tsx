import { HardDrive } from "lucide-react";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import type { BackupManagementController } from "./useBackupManagement.ts";

export function DataRootMigrationPanel({
  controller,
  onPathError,
}: {
  controller: BackupManagementController;
  onPathError: (message: string) => void;
}) {
  const storage = controller.runtimeStorage;
  if (!storage) return null;

  return (
    <section className="backup-recovery-card" aria-label="运行数据目录迁移">
      <div className="section-header">
        <div>
          <h3>运行数据目录</h3>
          <p className="section-description">业务数据库、附件和运行配置统一存放在可迁移的数据根目录。</p>
        </div>
        <button
          className="command-button secondary"
          type="button"
          disabled={!controller.canManageSettings || !storage.migrationSupported || controller.isBusy}
          title={storage.portable ? "便携版请通过复制完整程序目录迁移" : "选择新的空目录，重启后安全迁移"}
          onClick={() => void controller.chooseNewDataRoot()}
        >
          <HardDrive size={17} aria-hidden="true" />
          <span>更换数据目录</span>
        </button>
      </div>
      <div className="detail-grid runtime-detail-grid">
        <div className="detail-item detail-item-wide">
          <span>当前业务数据目录</span>
          <div className="detail-value-row">
            <strong title={storage.dataRoot}>{storage.dataRoot}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(storage.dataRoot, "打开业务数据目录", onPathError)}</div>
          </div>
        </div>
        <div className="detail-item detail-item-wide">
          <span>存储模式</span>
          <strong title={storage.storagePolicy}>{storage.portable ? "便携版 · 程序旁存储" : "安装版 · 独立目录"}</strong>
        </div>
      </div>
    </section>
  );
}
