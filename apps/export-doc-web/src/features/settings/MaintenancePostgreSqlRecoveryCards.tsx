import { useRef } from "react";
import { Database, Download, RotateCcw, ShieldCheck, Upload } from "lucide-react";
import type { ApiServerMigrationStatusResponse } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { formatRuntimeDate } from "./settingsFormatters.ts";

export function PostgreSqlDatabaseRecoveryCard({
  canManageSettings,
  busy,
  selectedBackup,
  adminPassword,
  restoreConfirmation,
  uploadFile,
  canRestoreSelected,
  canRestoreUploaded,
  onAdminPasswordChange,
  onRestoreConfirmationChange,
  onUploadFileChange,
  onDownload,
  onRestoreSelected,
  onRestoreUploaded,
}: {
  canManageSettings: boolean;
  busy: boolean;
  selectedBackup: string;
  adminPassword: string;
  restoreConfirmation: string;
  uploadFile: File | null;
  canRestoreSelected: boolean;
  canRestoreUploaded: boolean;
  onAdminPasswordChange: (value: string) => void;
  onRestoreConfirmationChange: (value: string) => void;
  onUploadFileChange: (file: File | null) => void;
  onDownload: () => void;
  onRestoreSelected: () => void;
  onRestoreUploaded: () => void;
}) {
  const uploadInputRef = useRef<HTMLInputElement | null>(null);

  return (
    <section className="backup-recovery-card" aria-label="网页端 PostgreSQL 备份恢复">
      <div className="section-header">
        <div>
          <h3>网页端备份与恢复</h3>
          <p className="section-description">备份文件保存在运行目录，可直接下载；恢复会先保留安全备份，再在服务启动前恢复数据库。</p>
        </div>
        <Database size={22} aria-hidden="true" />
      </div>
      <div className="backup-action-grid">
        <label>
          <span>管理员当前密码</span>
          <input
            type="password"
            autoComplete="current-password"
            value={adminPassword}
            disabled={!canManageSettings || busy}
            onChange={(event) => onAdminPasswordChange(event.target.value)}
          />
          <small>恢复前必须重新验证当前登录管理员身份。</small>
        </label>
        <button
          className="command-button secondary"
          type="button"
          disabled={!canManageSettings || !selectedBackup || busy}
          onClick={onDownload}
        >
          <Download size={17} aria-hidden="true" />
          <span>下载所选备份</span>
        </button>
        <label>
          <span>恢复确认文本</span>
          <input
            value={restoreConfirmation}
            disabled={!canManageSettings || busy}
            placeholder="RESTORE DATABASE"
            onChange={(event) => onRestoreConfirmationChange(event.target.value)}
          />
        </label>
        <button
          className="command-button danger-command"
          type="button"
          disabled={!canRestoreSelected}
          onClick={onRestoreSelected}
        >
          <RotateCcw size={17} aria-hidden="true" />
          <span>恢复所选备份</span>
        </button>
        <label>
          <span>上传 .dump 恢复</span>
          <input
            ref={uploadInputRef}
            hidden
            type="file"
            accept=".dump"
            onChange={(event) => {
              onUploadFileChange(event.currentTarget.files?.[0] ?? null);
              event.currentTarget.value = "";
            }}
          />
          <button
            className="command-button secondary"
            type="button"
            disabled={!canManageSettings || busy}
            onClick={() => uploadInputRef.current?.click()}
          >
            <Upload size={17} aria-hidden="true" />
            <span>{uploadFile?.name || "选择 .dump 文件"}</span>
          </button>
        </label>
        <button
          className="command-button danger-command"
          type="button"
          disabled={!canRestoreUploaded}
          onClick={onRestoreUploaded}
        >
          <RotateCcw size={17} aria-hidden="true" />
          <span>恢复上传文件</span>
        </button>
      </div>
    </section>
  );
}

export function ServerMigrationCard({
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
    <section className="backup-recovery-card" aria-label="完整服务器迁移">
      <div className="section-header">
        <div>
          <h3>完整服务器迁移</h3>
          <p className="section-description">包含数据库、印章、唛头图片、用户模板、配置、主密钥和单一窗口运行数据；不包含日志、缓存、许可证或 TLS/Certbot 证书。</p>
        </div>
        <ShieldCheck size={22} aria-hidden="true" />
      </div>
      {status ? <InlineNotice tone={status.pendingRestore ? "warning" : status.supported && status.toolsReady ? "info" : "warning"}>
        {status.message}
        {status.restorePhase ? ` 当前阶段：${formatRestorePhase(status.restorePhase)}。` : ""}
        {status.restoreDetail ? ` ${status.restoreDetail}` : ""}
        {status.restoreUpdatedAtUtc ? ` 更新时间：${formatRuntimeDate(status.restoreUpdatedAtUtc)}。` : ""}
      </InlineNotice> : null}
      <div className="backup-action-grid">
        <label>
          <span>管理员当前密码</span>
          <input
            type="password"
            autoComplete="current-password"
            value={adminPassword}
            disabled={!canManageSettings || busy}
            onChange={(event) => onAdminPasswordChange(event.target.value)}
          />
          <small>创建或恢复完整迁移包前都需要重新认证。</small>
        </label>
        <label>
          <span>新迁移包密码</span>
          <input
            type="password"
            autoComplete="new-password"
            value={packagePassword}
            disabled={!canManageSettings || busy}
            placeholder="至少12位，含大小写、数字和符号"
            onChange={(event) => onPackagePasswordChange(event.target.value)}
          />
        </label>
        <label>
          <span>再次输入密码</span>
          <input
            type="password"
            autoComplete="new-password"
            value={packagePasswordConfirmation}
            disabled={!canManageSettings || busy}
            onChange={(event) => onPackagePasswordConfirmationChange(event.target.value)}
          />
        </label>
        <label>
          <span>创建确认文本</span>
          <input
            value={createConfirmation}
            disabled={!canManageSettings || busy}
            placeholder="MIGRATE"
            onChange={(event) => onCreateConfirmationChange(event.target.value)}
          />
        </label>
        <button className="command-button" type="button" disabled={!canCreate} onClick={onCreate}>
          <Download size={17} aria-hidden="true" />
          <span>{createPending ? "正在创建迁移包" : "创建并下载迁移包"}</span>
        </button>
        <label>
          <span>待恢复迁移包</span>
          <input
            ref={restoreInputRef}
            hidden
            type="file"
            accept=".edmmigration"
            onChange={(event) => {
              onRestoreFileChange(event.currentTarget.files?.[0] ?? null);
              event.currentTarget.value = "";
            }}
          />
          <button
            className="command-button secondary"
            type="button"
            disabled={!canManageSettings || busy}
            onClick={() => restoreInputRef.current?.click()}
          >
            <Upload size={17} aria-hidden="true" />
            <span>{restoreFile?.name || "选择 .edmmigration 文件"}</span>
          </button>
        </label>
        <label>
          <span>迁移包密码</span>
          <input
            type="password"
            autoComplete="current-password"
            value={restorePassword}
            disabled={!canManageSettings || busy || !restoreFile}
            onChange={(event) => onRestorePasswordChange(event.target.value)}
          />
        </label>
        <label>
          <span>恢复确认文本</span>
          <input
            value={restoreConfirmation}
            disabled={!canManageSettings || busy || !restoreFile}
            placeholder="MIGRATE"
            onChange={(event) => onRestoreConfirmationChange(event.target.value)}
          />
        </label>
        <button className="command-button danger-command" type="button" disabled={!canRestore} onClick={onRestore}>
          <RotateCcw size={17} aria-hidden="true" />
          <span>安排完整迁移</span>
        </button>
      </div>
      <p className="settings-helper-text">{status?.storagePolicy}</p>
    </section>
  );
}

function formatRestorePhase(value: string) {
  const labels: Record<string, string> = {
    pending: "等待服务重启",
    validating: "验证迁移包",
    "safety-backup": "创建安全备份",
    "applying-database": "恢复数据库",
    "applying-files": "替换运行文件",
    "rolling-back": "自动回滚",
    completed: "已完成",
    failed: "失败",
  };
  return labels[value.trim().toLowerCase()] ?? value;
}
