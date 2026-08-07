import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, RefreshCw, RotateCcw } from "lucide-react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { downloadJobResultWhenReady, startDownloadFromTicket, waitForJobCompletion } from "../../ui/downloadJobResult.ts";
import { SelectField } from "../../ui/FormFields.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";
import { parseStringArray } from "./settingsValueUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { PostgreSqlDatabaseRecoveryCard, ServerMigrationCard } from "./MaintenancePostgreSqlRecoveryCards.tsx";

function isStrongMigrationPassword(value: string) {
  return value.length >= 12 &&
    value.length <= 128 &&
    /[A-Z]/.test(value) &&
    /[a-z]/.test(value) &&
    /\d/.test(value) &&
    /[^A-Za-z0-9]/.test(value);
}

export function PostgreSqlMaintenancePanel({
  client,
  canManageSettings,
  onPathError,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  onPathError: (message: string) => void;
}) {
  const runAbortableOperation = useAbortableOperation();
  const queryClient = useQueryClient();
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [selectedBackup, setSelectedBackup] = useState("");
  const [targetDatabase, setTargetDatabase] = useState("");
  const [applicationRole, setApplicationRole] = useState("");
  const [oldOwnerRoles, setOldOwnerRoles] = useState("");
  const [lastRestorePlanPath, setLastRestorePlanPath] = useState("");
  const [databaseAdminPassword, setDatabaseAdminPassword] = useState("");
  const [migrationPassword, setMigrationPassword] = useState("");
  const [migrationPasswordConfirmation, setMigrationPasswordConfirmation] = useState("");
  const [migrationAdminPassword, setMigrationAdminPassword] = useState("");
  const [migrationCreateConfirmation, setMigrationCreateConfirmation] = useState("");
  const [migrationRestoreFile, setMigrationRestoreFile] = useState<File | null>(null);
  const [migrationRestorePassword, setMigrationRestorePassword] = useState("");
  const [migrationRestoreConfirmation, setMigrationRestoreConfirmation] = useState("");
  const [databaseRestoreConfirmation, setDatabaseRestoreConfirmation] = useState("");
  const [databaseUploadFile, setDatabaseUploadFile] = useState<File | null>(null);

  const postgreSqlQuery = useQuery({
    queryKey: queryKeys.postgreSqlMaintenanceBackups(),
    queryFn: ({ signal }) => client.listPostgreSqlPhysicalBackups({ signal }),
    enabled: canManageSettings,
  });

  const migrationStatusQuery = useQuery({
    queryKey: queryKeys.serverMigrationStatus(),
    queryFn: ({ signal }) => client.getServerMigrationStatus({ signal }),
    enabled: canManageSettings,
  });

  useEffect(() => {
    const status = postgreSqlQuery.data?.status;
    if (status) {
      setTargetDatabase((current) => current || status.database || "");
      setApplicationRole((current) => current || status.username || "");
    }

    const backups = postgreSqlQuery.data?.backups ?? [];
    if (backups.length === 0) {
      setSelectedBackup("");
      return;
    }

    if (!selectedBackup || !backups.some((backup) => backup.fileName === selectedBackup)) {
      setSelectedBackup(backups[0].fileName);
    }
  }, [postgreSqlQuery.data, selectedBackup]);

  useEffect(() => {
    if (postgreSqlQuery.isError) {
      setMessage(readApiError(postgreSqlQuery.error));
      setSuccessMessage(null);
    }
  }, [postgreSqlQuery.error, postgreSqlQuery.isError]);

  const createMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const job = await client.createPostgreSqlPhysicalBackup({ signal });
      return waitForJobCompletion(client, job, {
        timeoutMs: 60 * 60 * 1000,
        pollIntervalMs: 2_000,
        signal,
        timeoutMessage: "数据库备份仍在后台运行，可稍后到任务中心查看，完成后刷新备份列表。",
      });
    }),
    onSuccess: async () => {
      setMessage(null);
      setSuccessMessage("PostgreSQL 团队库物理备份已创建并校验完成。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.postgreSqlMaintenanceBackups() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const restorePlanMutation = useMutation({
    mutationFn: () =>
      client.createPostgreSqlRestorePlan({
        body: {
          backupFileName: selectedBackup,
          targetDatabase: targetDatabase.trim(),
          applicationRole: applicationRole.trim(),
          oldOwnerRoles: parseStringArray(oldOwnerRoles),
        },
      }),
    onSuccess: (response) => {
      setLastRestorePlanPath(response.planRoot);
      setMessage(null);
      setSuccessMessage(response.message || "PostgreSQL 还原与权限改派脚本已生成。");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const downloadMigrationMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const job = await client.createServerMigrationPackage({
        body: {
          password: migrationPassword,
          adminPassword: migrationAdminPassword,
          confirmationText: migrationCreateConfirmation.trim(),
        },
      }, { signal });
      return downloadJobResultWhenReady(
        client,
        job,
        `export-doc-manager-server-migration-${new Date().toISOString().slice(0, 10)}.edmmigration`,
        { timeoutMs: 60 * 60 * 1000, pollIntervalMs: 2_000, signal },
      );
    }),
    onSuccess: () => {
      setMigrationPassword("");
      setMigrationPasswordConfirmation("");
      setMigrationAdminPassword("");
      setMigrationCreateConfirmation("");
      setMessage(null);
      setSuccessMessage("完整服务器迁移包已交给浏览器下载，请将密码单独保管。");
    },
    onError: (error) => {
      setMigrationPassword("");
      setMigrationPasswordConfirmation("");
      setMigrationAdminPassword("");
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const restoreMigrationMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const authorization = await client.authorizeServerMigrationOperation({
        body: {
          action: "restore-server",
          adminPassword: migrationAdminPassword,
        },
      }, { signal });
      return client.stageServerMigrationRestore(
        { body: migrationRestoreFile as File },
        {
          signal,
          headers: {
            "X-ExportDocManager-Sensitive-Operation-Ticket": authorization.ticket,
            "X-ExportDocManager-Migration-Password": migrationRestorePassword,
            "X-ExportDocManager-Migration-File-Name": migrationRestoreFile?.name ?? "migration.edmmigration",
            "X-ExportDocManager-Restore-Confirmation": migrationRestoreConfirmation.trim(),
          },
        },
      );
    }),
    onSuccess: async (response) => {
      setMigrationAdminPassword("");
      setMigrationRestorePassword("");
      setMigrationRestoreConfirmation("");
      setMigrationRestoreFile(null);
      setMessage(null);
      setSuccessMessage(response.message || "服务器迁移已排队，请重启服务完成恢复。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.serverMigrationStatus() });
    },
    onError: (error) => {
      setMigrationAdminPassword("");
      setMigrationRestorePassword("");
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const databaseRestoreMutation = useMutation({
    mutationFn: () => runAbortableOperation((signal) => client.restorePostgreSqlPhysicalBackup(
      {
        body: {
          backupFileName: selectedBackup,
          adminPassword: databaseAdminPassword,
          confirmationText: databaseRestoreConfirmation.trim(),
        },
      },
      { signal },
    )),
    onSuccess: async (response) => {
      setDatabaseAdminPassword("");
      setDatabaseRestoreConfirmation("");
      setMessage(null);
      setSuccessMessage(response.message || "PostgreSQL 数据库恢复已排队，请重启服务完成恢复。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.serverMigrationStatus() });
    },
    onError: (error) => {
      setDatabaseAdminPassword("");
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const downloadDatabaseMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const ticket = await client.createPostgreSqlPhysicalBackupDownloadTicket(
        { fileName: selectedBackup },
        { signal },
      );
      startDownloadFromTicket(client, ticket, selectedBackup || "exportdoc-postgresql.dump");
    }),
    onSuccess: () => {
      setMessage(null);
      setSuccessMessage("PostgreSQL 备份已交给浏览器下载。");
    },
    onError: (error) => { setMessage(readApiError(error)); setSuccessMessage(null); },
  });

  const uploadDatabaseRestoreMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const authorization = await client.authorizeServerMigrationOperation({
        body: {
          action: "restore-database",
          adminPassword: databaseAdminPassword,
        },
      }, { signal });
      return client.uploadAndRestorePostgreSqlPhysicalBackup(
        { body: databaseUploadFile as File },
        {
          signal,
          headers: {
            "X-ExportDocManager-Sensitive-Operation-Ticket": authorization.ticket,
            "X-ExportDocManager-PostgreSql-Backup-File-Name": databaseUploadFile?.name ?? "database.dump",
            "X-ExportDocManager-Restore-Confirmation": databaseRestoreConfirmation.trim(),
          },
        },
      );
    }),
    onSuccess: async (response) => {
      setDatabaseAdminPassword("");
      setDatabaseUploadFile(null);
      setDatabaseRestoreConfirmation("");
      setMessage(null);
      setSuccessMessage(response.message || "上传的 PostgreSQL 数据库恢复已排队，请重启服务完成恢复。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.serverMigrationStatus() });
    },
    onError: (error) => {
      setDatabaseAdminPassword("");
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const status = postgreSqlQuery.data?.status;
  const backups = postgreSqlQuery.data?.backups ?? [];
  const isBusy = postgreSqlQuery.isFetching || migrationStatusQuery.isFetching || createMutation.isPending || restorePlanMutation.isPending ||
    downloadMigrationMutation.isPending || restoreMigrationMutation.isPending || databaseRestoreMutation.isPending ||
    downloadDatabaseMutation.isPending || uploadDatabaseRestoreMutation.isPending;
  const canCreate = canManageSettings && Boolean(status?.postgreSqlConfigured) && Boolean(status?.toolsReady) && !isBusy;
  const canCreatePlan =
    canManageSettings &&
    Boolean(selectedBackup) &&
    Boolean(targetDatabase.trim()) &&
    Boolean(applicationRole.trim()) &&
    !isBusy;
  const migrationStatus = migrationStatusQuery.data;
  const canCreateMigration = canManageSettings && Boolean(migrationStatus?.supported) && Boolean(migrationStatus?.toolsReady) &&
    Boolean(migrationAdminPassword) && migrationCreateConfirmation.trim() === "MIGRATE" &&
    isStrongMigrationPassword(migrationPassword) && migrationPassword === migrationPasswordConfirmation && !isBusy;
  const canRestoreMigration = canManageSettings && Boolean(migrationRestoreFile) && isStrongMigrationPassword(migrationRestorePassword) &&
    Boolean(migrationAdminPassword) && migrationRestoreConfirmation.trim() === "MIGRATE" && !isBusy;
  const canRestoreDatabase = canManageSettings && Boolean(selectedBackup) && Boolean(databaseAdminPassword) &&
    databaseRestoreConfirmation.trim() === "RESTORE DATABASE" && !isBusy;
  const canUploadDatabase = canManageSettings && Boolean(databaseUploadFile) && Boolean(databaseAdminPassword) &&
    databaseRestoreConfirmation.trim() === "RESTORE DATABASE" && !isBusy;

  return (
    <section className="form-section backup-management-section" aria-label="PostgreSQL 团队库维护">
      <div className="section-header">
        <h2>PostgreSQL 团队库维护</h2>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新 PostgreSQL 备份" aria-label="刷新 PostgreSQL 备份"
            disabled={!canManageSettings || isBusy}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              void postgreSqlQuery.refetch();
            }}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button
            className="command-button"
            type="button"
            disabled={!canCreate}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              createMutation.mutate();
            }}
          >
            <Archive size={17} aria-hidden="true" />
            <span>{createMutation.isPending ? "正在后台备份" : "创建物理备份"}</span>
          </button>
        </div>
      </div>
      {!status?.postgreSqlSelected ? <InlineNotice tone="info">当前为 SQLite 单机模式，PostgreSQL 团队库维护保持停用。</InlineNotice> : null}
      {status?.postgreSqlSelected && !status.postgreSqlConfigured ? <InlineNotice tone="info">PostgreSQL 团队库连接信息尚未完整配置。</InlineNotice> : null}
      {status?.postgreSqlConfigured && !status.toolsReady ? (
        <InlineNotice tone="info">团队数据库维护组件未安装完整，请联系系统管理员或软件服务商处理。</InlineNotice>
      ) : null}
      {message ? <InlineNotice tone="error" title="数据库维护失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <div className="detail-grid runtime-detail-grid">
        <div className="detail-item">
          <span>团队库模式</span>
          <strong>{status?.postgreSqlConfigured ? "已配置" : status?.postgreSqlSelected ? "未完整" : "未启用"}</strong>
        </div>
        <div className="detail-item">
          <span>客户端工具</span>
          <strong>{status?.toolsReady ? "已就绪" : "缺失"}</strong>
        </div>
        <div className="detail-item">
          <span>目标库</span>
          <strong title={status?.database || "-"}>{status?.database || "-"}</strong>
        </div>
        <div className="detail-item">
          <span>应用账号</span>
          <strong title={status?.username || "-"}>{status?.username || "-"}</strong>
        </div>
        <div className="detail-item detail-item-wide">
          <span>物理备份目录</span>
          <div className="detail-value-row">
            <strong title={status?.backupRoot || "-"}>{status?.backupRoot || "-"}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(status?.backupRoot, "打开 PostgreSQL 备份目录", onPathError)}</div>
          </div>
        </div>
        <div className="detail-item detail-item-wide">
          <span>工具目录</span>
          <div className="detail-value-row">
            <strong title={status?.toolBinRoot || "-"}>{status?.toolBinRoot || "-"}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(status?.toolBinRoot, "打开 PostgreSQL 工具目录", onPathError)}</div>
          </div>
        </div>
      </div>
      <div className="backup-action-grid">
        <SelectField
          label="备份文件"
          value={selectedBackup}
          disabled={!canManageSettings || isBusy || backups.length === 0}
          options={backups.map((backup) => ({ value: backup.fileName, label: backup.fileName }))}
          onChange={setSelectedBackup}
        />
        <label>
          <span>目标数据库</span>
          <input
            value={targetDatabase}
            disabled={!canManageSettings || isBusy}
            onChange={(event) => setTargetDatabase(event.target.value)}
          />
        </label>
        <label>
          <span>应用账号</span>
          <input
            value={applicationRole}
            disabled={!canManageSettings || isBusy}
            onChange={(event) => setApplicationRole(event.target.value)}
          />
        </label>
        <button
          className="command-button"
          type="button"
          disabled={!canCreatePlan}
          onClick={() => {
            setMessage(null);
            setSuccessMessage(null);
            restorePlanMutation.mutate();
          }}
        >
          <RotateCcw size={17} aria-hidden="true" />
          <span>生成还原脚本</span>
        </button>
      </div>
      <PostgreSqlDatabaseRecoveryCard
        canManageSettings={canManageSettings}
        busy={isBusy}
        selectedBackup={selectedBackup}
        adminPassword={databaseAdminPassword}
        restoreConfirmation={databaseRestoreConfirmation}
        uploadFile={databaseUploadFile}
        canRestoreSelected={canRestoreDatabase}
        canRestoreUploaded={canUploadDatabase}
        onAdminPasswordChange={setDatabaseAdminPassword}
        onRestoreConfirmationChange={setDatabaseRestoreConfirmation}
        onUploadFileChange={setDatabaseUploadFile}
        onDownload={() => downloadDatabaseMutation.mutate()}
        onRestoreSelected={() => databaseRestoreMutation.mutate()}
        onRestoreUploaded={() => uploadDatabaseRestoreMutation.mutate()}
      />
      <ServerMigrationCard
        canManageSettings={canManageSettings}
        busy={isBusy}
        status={migrationStatus}
        adminPassword={migrationAdminPassword}
        packagePassword={migrationPassword}
        packagePasswordConfirmation={migrationPasswordConfirmation}
        createConfirmation={migrationCreateConfirmation}
        restoreFile={migrationRestoreFile}
        restorePassword={migrationRestorePassword}
        restoreConfirmation={migrationRestoreConfirmation}
        canCreate={canCreateMigration}
        canRestore={canRestoreMigration}
        createPending={downloadMigrationMutation.isPending}
        onAdminPasswordChange={setMigrationAdminPassword}
        onPackagePasswordChange={setMigrationPassword}
        onPackagePasswordConfirmationChange={setMigrationPasswordConfirmation}
        onCreateConfirmationChange={setMigrationCreateConfirmation}
        onRestoreFileChange={setMigrationRestoreFile}
        onRestorePasswordChange={setMigrationRestorePassword}
        onRestoreConfirmationChange={setMigrationRestoreConfirmation}
        onCreate={() => downloadMigrationMutation.mutate()}
        onRestore={() => restoreMigrationMutation.mutate()}
      />
      <details className="maintenance-advanced-details">
        <summary>高级还原选项</summary>
        <label className="textarea-field settings-textarea-field">
          <span>原数据库所有者（可选）</span>
          <textarea
            value={oldOwnerRoles}
            disabled={!canManageSettings || isBusy}
            placeholder={"每行填写一个原账号"}
            onChange={(event) => setOldOwnerRoles(event.target.value)}
          />
          <small>仅在数据库从其他账号迁移而来时填写。</small>
        </label>
      </details>
      {lastRestorePlanPath ? (
        <InlineNotice tone="info" action={renderOpenPathAction(lastRestorePlanPath, "打开还原计划目录", onPathError)}>{lastRestorePlanPath}</InlineNotice>
      ) : null}
      <ResponsiveTableFrame className="backup-table-frame" label="PostgreSQL 团队库物理备份列表">
        <table className="backup-table" aria-label="PostgreSQL 团队库物理备份列表">
          <thead>
            <tr>
              <th>文件</th>
              <th>大小</th>
              <th>创建时间</th>
              <th>路径</th>
            </tr>
          </thead>
          <tbody>
            {backups.length > 0 ? (
              backups.map((backup) => (
                <tr key={backup.fullPath || backup.fileName}>
                  <td>{backup.fileName}</td>
                  <td>{formatBytes(backup.sizeBytes)}</td>
                  <td>{formatRuntimeDate(backup.createdAt)}</td>
                  <td>
                    <div className="table-path-cell">
                      <span title={backup.fullPath}>{backup.fullPath || "-"}</span>
                      {renderOpenPathAction(backup.fullPath, "打开 PostgreSQL 备份", onPathError)}
                    </div>
                  </td>
                </tr>
              ))
            ) : (
              <tr>
                <td className="empty-cell" colSpan={4}>
                  {postgreSqlQuery.isFetching ? "加载中" : "暂无 PostgreSQL 物理备份"}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </ResponsiveTableFrame>
    </section>
  );
}
