import { RotateCcw, ShieldCheck } from "lucide-react";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { formatManagedPath } from "./settingsFormatters.ts";
import type { BackupManagementController } from "./useBackupManagement.ts";

export function DisasterRecoveryPanel({
  controller,
  onPathError,
}: {
  controller: BackupManagementController;
  onPathError: (message: string) => void;
}) {
  const status = controller.disasterRecoveryStatus;
  return (
    <section className="backup-recovery-card" aria-label="持卡机灾难恢复">
      <div className="section-header">
        <div>
          <h3>持卡机灾难恢复</h3>
          <p className="section-description">独立加密包用于整机损坏或更换持卡机，不等同于普通数据库 ZIP 备份。</p>
        </div>
        <ShieldCheck size={22} aria-hidden="true" />
      </div>
      {status ? (
        <InlineNotice tone={status.pendingRestore ? "warning" : status.supported ? "info" : "warning"} title={status.pendingRestore ? "恢复任务等待重启" : "恢复包边界"}>
          {status.message} 恢复包不携带许可证或机器绑定，恢复后必须按当前机器码重新激活。
        </InlineNotice>
      ) : null}
      <div className="detail-grid runtime-detail-grid">
        <div className="detail-item detail-item-wide">
          <span>恢复包目录</span>
          <div className="detail-value-row">
            <strong title={formatManagedPath(status?.recoveryRoot)}>{formatManagedPath(status?.recoveryRoot)}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(status?.recoveryRoot, "打开恢复包目录", onPathError)}</div>
          </div>
        </div>
        {controller.lastRecoveryPackagePath ? (
          <div className="detail-item detail-item-wide">
            <span>本次生成</span>
            <div className="detail-value-row">
              <strong title={controller.lastRecoveryPackagePath}>{controller.lastRecoveryPackagePath}</strong>
              <div className="detail-item-actions">{renderOpenPathAction(controller.lastRecoveryPackagePath, "打开恢复包", onPathError)}</div>
            </div>
          </div>
        ) : null}
      </div>
      <div className="backup-action-grid">
        <label>
          <span>新恢复包密码</span>
          <input type="password" autoComplete="new-password" value={controller.recoveryPassword} disabled={!controller.canManageSettings || controller.isBusy || !status?.supported} placeholder="至少 12 位，含大小写、数字和符号" onChange={(event) => controller.setRecoveryPassword(event.target.value)} />
        </label>
        <label>
          <span>再次输入密码</span>
          <input type="password" autoComplete="new-password" value={controller.recoveryPasswordConfirmation} disabled={!controller.canManageSettings || controller.isBusy || !status?.supported} onChange={(event) => controller.setRecoveryPasswordConfirmation(event.target.value)} />
        </label>
        <button className="command-button" type="button" disabled={!controller.canCreateRecovery} onClick={controller.createRecoveryPackage}>
          <ShieldCheck size={17} aria-hidden="true" />
          <span>创建加密恢复包</span>
        </button>
      </div>
      <div className="backup-action-grid">
        <label>
          <span>待恢复文件</span>
          <input value={controller.recoveryPackagePath} readOnly placeholder="请选择 .edmrecovery 文件" />
        </label>
        <button className="command-button secondary" type="button" disabled={!controller.canManageSettings || controller.isBusy || !status?.supported} onClick={() => void controller.chooseRecoveryPackage()}>
          选择恢复包
        </button>
        <label>
          <span>恢复包密码</span>
          <input type="password" autoComplete="current-password" value={controller.recoveryRestorePassword} disabled={!controller.canManageSettings || controller.isBusy || !controller.recoveryPackagePath} onChange={(event) => controller.setRecoveryRestorePassword(event.target.value)} />
        </label>
        <label>
          <span>确认文本</span>
          <input value={controller.recoveryRestoreConfirmation} disabled={!controller.canManageSettings || controller.isBusy || !controller.recoveryPackagePath} placeholder="RECOVER" onChange={(event) => controller.setRecoveryRestoreConfirmation(event.target.value)} />
        </label>
        <button className="command-button danger-command" type="button" disabled={!controller.canRestoreRecovery} onClick={controller.restoreRecoveryPackage}>
          <RotateCcw size={17} aria-hidden="true" />
          <span>安排灾难恢复</span>
        </button>
      </div>
    </section>
  );
}
