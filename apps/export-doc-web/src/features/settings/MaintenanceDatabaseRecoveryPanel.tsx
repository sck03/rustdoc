import { useRef } from "react";
import { Database, Download, RotateCcw, Upload } from "lucide-react";
import type { ApiPostgreSqlPhysicalBackupListResponse } from "../../api/index.ts";
import { SelectField } from "../../ui/FormFields.tsx";

export function MaintenanceDatabaseRecoveryPanel({
  canManageSettings,
  busy,
  backups,
  selectedBackup,
  targetDatabase,
  applicationRole,
  oldOwnerRoles,
  adminPassword,
  restoreConfirmation,
  uploadFile,
  canRestoreSelected,
  canRestoreUploaded,
  canCreatePlan,
  onSelectedBackupChange,
  onTargetDatabaseChange,
  onApplicationRoleChange,
  onOldOwnerRolesChange,
  onAdminPasswordChange,
  onRestoreConfirmationChange,
  onUploadFileChange,
  onDownload,
  onRestoreSelected,
  onRestoreUploaded,
  onCreatePlan,
}: {
  canManageSettings: boolean;
  busy: boolean;
  backups: ApiPostgreSqlPhysicalBackupListResponse["backups"];
  selectedBackup: string;
  targetDatabase: string;
  applicationRole: string;
  oldOwnerRoles: string;
  adminPassword: string;
  restoreConfirmation: string;
  uploadFile: File | null;
  canRestoreSelected: boolean;
  canRestoreUploaded: boolean;
  canCreatePlan: boolean;
  onSelectedBackupChange: (value: string) => void;
  onTargetDatabaseChange: (value: string) => void;
  onApplicationRoleChange: (value: string) => void;
  onOldOwnerRolesChange: (value: string) => void;
  onAdminPasswordChange: (value: string) => void;
  onRestoreConfirmationChange: (value: string) => void;
  onUploadFileChange: (file: File | null) => void;
  onDownload: () => void;
  onRestoreSelected: () => void;
  onRestoreUploaded: () => void;
  onCreatePlan: () => void;
}) {
  const uploadInputRef = useRef<HTMLInputElement | null>(null);
  return (
    <section className="form-section maintenance-database-recovery-panel" aria-label="PostgreSQL 数据库恢复">
      <div className="section-header"><div><h3>数据库恢复</h3><p className="section-description">恢复前会保留安全备份；可恢复服务器已有备份或上传新的 custom-format .dump。</p></div><Database size={22} aria-hidden="true" /></div>
      <div className="backup-action-grid">
        <SelectField label="备份文件" value={selectedBackup} disabled={!canManageSettings || busy || backups.length === 0} options={backups.map((backup) => ({ value: backup.fileName, label: backup.fileName }))} onChange={onSelectedBackupChange} />
        <label><span>目标数据库</span><input value={targetDatabase} disabled={!canManageSettings || busy} onChange={(event) => onTargetDatabaseChange(event.target.value)} /></label>
        <label><span>应用账号</span><input value={applicationRole} disabled={!canManageSettings || busy} onChange={(event) => onApplicationRoleChange(event.target.value)} /></label>
        <button className="command-button" type="button" disabled={!canCreatePlan} onClick={onCreatePlan}><RotateCcw size={17} aria-hidden="true" /><span>生成还原脚本</span></button>
        <label><span>管理员当前密码</span><input type="password" autoComplete="current-password" value={adminPassword} disabled={!canManageSettings || busy} onChange={(event) => onAdminPasswordChange(event.target.value)} /><small>恢复前必须重新验证当前登录管理员身份。</small></label>
        <button className="command-button secondary" type="button" disabled={!canManageSettings || !selectedBackup || busy} onClick={onDownload}><Download size={17} aria-hidden="true" /><span>下载所选备份</span></button>
        <label><span>恢复确认文本</span><input value={restoreConfirmation} disabled={!canManageSettings || busy} placeholder="RESTORE DATABASE" onChange={(event) => onRestoreConfirmationChange(event.target.value)} /></label>
        <button className="command-button danger-command" type="button" disabled={!canRestoreSelected} onClick={onRestoreSelected}><RotateCcw size={17} aria-hidden="true" /><span>恢复所选备份</span></button>
        <label>
          <span>上传 .dump 恢复</span>
          <input ref={uploadInputRef} hidden type="file" accept=".dump" onChange={(event) => { onUploadFileChange(event.currentTarget.files?.[0] ?? null); event.currentTarget.value = ""; }} />
          <button className="command-button secondary" type="button" disabled={!canManageSettings || busy} onClick={() => uploadInputRef.current?.click()}><Upload size={17} aria-hidden="true" /><span>{uploadFile?.name || "选择 .dump 文件"}</span></button>
        </label>
        <button className="command-button danger-command" type="button" disabled={!canRestoreUploaded} onClick={onRestoreUploaded}><RotateCcw size={17} aria-hidden="true" /><span>恢复上传文件</span></button>
      </div>
      <details className="maintenance-advanced-details"><summary>高级还原选项</summary><label className="textarea-field settings-textarea-field"><span>原数据库所有者（可选）</span><textarea value={oldOwnerRoles} disabled={!canManageSettings || busy} placeholder="每行填写一个原账号" onChange={(event) => onOldOwnerRolesChange(event.target.value)} /><small>仅在数据库从其他账号迁移而来时填写。</small></label></details>
    </section>
  );
}
