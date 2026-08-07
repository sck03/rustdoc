import { useRef } from "react";
import { Download, RotateCcw, ShieldCheck, Upload } from "lucide-react";
import type { ApiServerMigrationStatusResponse } from "../../api/index.ts";

export function MaintenanceFullMigrationPanel({
  canManageSettings,
  busy,
  status,
  adminPassword,
  packagePassword,
  packagePasswordConfirmation,
  createConfirmation,
  restoreFile,
  restorePassword,
  restoreConfirmation,
  canCreate,
  canRestore,
  createPending,
  onAdminPasswordChange,
  onPackagePasswordChange,
  onPackagePasswordConfirmationChange,
  onCreateConfirmationChange,
  onRestoreFileChange,
  onRestorePasswordChange,
  onRestoreConfirmationChange,
  onCreate,
  onRestore,
}: {
  canManageSettings: boolean;
  busy: boolean;
  status: ApiServerMigrationStatusResponse | null | undefined;
  adminPassword: string;
  packagePassword: string;
  packagePasswordConfirmation: string;
  createConfirmation: string;
  restoreFile: File | null;
  restorePassword: string;
  restoreConfirmation: string;
  canCreate: boolean;
  canRestore: boolean;
  createPending: boolean;
  onAdminPasswordChange: (value: string) => void;
  onPackagePasswordChange: (value: string) => void;
  onPackagePasswordConfirmationChange: (value: string) => void;
  onCreateConfirmationChange: (value: string) => void;
  onRestoreFileChange: (file: File | null) => void;
  onRestorePasswordChange: (value: string) => void;
  onRestoreConfirmationChange: (value: string) => void;
  onCreate: () => void;
  onRestore: () => void;
}) {
  const restoreInputRef = useRef<HTMLInputElement | null>(null);
  return (
    <section className="form-section maintenance-full-migration-panel" aria-label="完整服务器迁移">
      <div className="section-header"><div><h3>完整迁移</h3><p className="section-description">包含数据库、印章、唛头图片、用户模板、配置、主密钥和单一窗口运行数据；不包含日志、缓存、许可证或 TLS/Certbot 证书。</p></div><ShieldCheck size={22} aria-hidden="true" /></div>
      <div className="backup-action-grid">
        <label><span>管理员当前密码</span><input type="password" autoComplete="current-password" value={adminPassword} disabled={!canManageSettings || busy} onChange={(event) => onAdminPasswordChange(event.target.value)} /><small>创建或恢复完整迁移包前都需要重新认证。</small></label>
        <label><span>新迁移包密码</span><input type="password" autoComplete="new-password" value={packagePassword} disabled={!canManageSettings || busy} placeholder="至少12位，含大小写、数字和符号" onChange={(event) => onPackagePasswordChange(event.target.value)} /></label>
        <label><span>再次输入密码</span><input type="password" autoComplete="new-password" value={packagePasswordConfirmation} disabled={!canManageSettings || busy} onChange={(event) => onPackagePasswordConfirmationChange(event.target.value)} /></label>
        <label><span>创建确认文本</span><input value={createConfirmation} disabled={!canManageSettings || busy} placeholder="MIGRATE" onChange={(event) => onCreateConfirmationChange(event.target.value)} /></label>
        <button className="command-button" type="button" disabled={!canCreate} onClick={onCreate}><Download size={17} aria-hidden="true" /><span>{createPending ? "正在创建迁移包" : "创建并下载迁移包"}</span></button>
        <label><span>待恢复迁移包</span><input ref={restoreInputRef} hidden type="file" accept=".edmmigration" onChange={(event) => { onRestoreFileChange(event.currentTarget.files?.[0] ?? null); event.currentTarget.value = ""; }} /><button className="command-button secondary" type="button" disabled={!canManageSettings || busy} onClick={() => restoreInputRef.current?.click()}><Upload size={17} aria-hidden="true" /><span>{restoreFile?.name || "选择 .edmmigration 文件"}</span></button></label>
        <label><span>迁移包密码</span><input type="password" autoComplete="current-password" value={restorePassword} disabled={!canManageSettings || busy || !restoreFile} onChange={(event) => onRestorePasswordChange(event.target.value)} /></label>
        <label><span>恢复确认文本</span><input value={restoreConfirmation} disabled={!canManageSettings || busy || !restoreFile} placeholder="MIGRATE" onChange={(event) => onRestoreConfirmationChange(event.target.value)} /></label>
        <button className="command-button danger-command" type="button" disabled={!canRestore} onClick={onRestore}><RotateCcw size={17} aria-hidden="true" /><span>安排完整迁移</span></button>
      </div>
      <p className="settings-helper-text">{status?.storagePolicy}</p>
    </section>
  );
}
