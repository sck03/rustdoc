import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { downloadJobResultWhenReady, startDownloadFromTicket, waitForJobCompletion } from "../../ui/downloadJobResult.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { parseStringArray } from "./settingsValueUtils.ts";
import { MaintenanceBackgroundTaskStatusPanel } from "./MaintenanceBackgroundTaskStatusPanel.tsx";
import { MaintenanceDatabaseBackupPanel } from "./MaintenanceDatabaseBackupPanel.tsx";
import { MaintenanceDatabaseRecoveryPanel } from "./MaintenanceDatabaseRecoveryPanel.tsx";
import { MaintenanceFullMigrationPanel } from "./MaintenanceFullMigrationPanel.tsx";

function isStrongMigrationPassword(value: string) {
  return value.length >= 12 && value.length <= 128 && /[A-Z]/.test(value) && /[a-z]/.test(value) && /\d/.test(value) && /[^A-Za-z0-9]/.test(value);
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
    refetchInterval: (query) => canManageSettings && query.state.data?.pendingRestore ? 5_000 : false,
  });

  useEffect(() => {
    const status = postgreSqlQuery.data?.status;
    if (status) {
      setTargetDatabase((current) => current || status.database || "");
      setApplicationRole((current) => current || status.username || "");
    }
    const backups = postgreSqlQuery.data?.backups ?? [];
    if (backups.length === 0) { setSelectedBackup(""); return; }
    if (!selectedBackup || !backups.some((backup) => backup.fileName === selectedBackup)) setSelectedBackup(backups[0].fileName);
  }, [postgreSqlQuery.data, selectedBackup]);

  useEffect(() => {
    if (postgreSqlQuery.isError) {
      setMessage(readApiError(postgreSqlQuery.error));
      setSuccessMessage(null);
    }
  }, [postgreSqlQuery.error, postgreSqlQuery.isError]);

  const fail = (error: unknown) => { setMessage(readApiError(error)); setSuccessMessage(null); };
  const clearFeedback = () => { setMessage(null); setSuccessMessage(null); };

  const createMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const job = await client.createPostgreSqlPhysicalBackup({ signal });
      return waitForJobCompletion(client, job, { timeoutMs: 60 * 60 * 1000, pollIntervalMs: 2_000, signal, timeoutMessage: "数据库备份仍在后台运行，可稍后到任务中心查看，完成后刷新备份列表。" });
    }),
    onSuccess: async () => { setSuccessMessage("PostgreSQL 团队库物理备份已创建并校验完成。"); setMessage(null); await queryClient.invalidateQueries({ queryKey: queryKeys.postgreSqlMaintenanceBackups() }); },
    onError: fail,
  });

  const restorePlanMutation = useMutation({
    mutationFn: () => client.createPostgreSqlRestorePlan({ body: { backupFileName: selectedBackup, targetDatabase: targetDatabase.trim(), applicationRole: applicationRole.trim(), oldOwnerRoles: parseStringArray(oldOwnerRoles) } }),
    onSuccess: (response) => { setLastRestorePlanPath(response.planRoot); setMessage(null); setSuccessMessage(response.message || "PostgreSQL 还原与权限改派脚本已生成。"); },
    onError: fail,
  });

  const downloadMigrationMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const job = await client.createServerMigrationPackage({ body: { password: migrationPassword, adminPassword: migrationAdminPassword, confirmationText: migrationCreateConfirmation.trim() } }, { signal });
      return downloadJobResultWhenReady(client, job, `export-doc-manager-server-migration-${new Date().toISOString().slice(0, 10)}.edmmigration`, { timeoutMs: 60 * 60 * 1000, pollIntervalMs: 2_000, signal });
    }),
    onSuccess: () => { setMigrationPassword(""); setMigrationPasswordConfirmation(""); setMigrationAdminPassword(""); setMigrationCreateConfirmation(""); setMessage(null); setSuccessMessage("完整服务器迁移包已交给浏览器下载，请将密码单独保管。"); },
    onError: (error) => { setMigrationPassword(""); setMigrationPasswordConfirmation(""); setMigrationAdminPassword(""); setMigrationCreateConfirmation(""); fail(error); },
  });

  const restoreMigrationMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const authorization = await client.authorizeServerMigrationOperation({ body: { action: "restore-server", adminPassword: migrationAdminPassword } }, { signal });
      return client.stageServerMigrationRestore({
        body: migrationRestoreFile as File,
        "X-ExportDocManager-Sensitive-Operation-Ticket": authorization.ticket,
        "X-ExportDocManager-Migration-Password": migrationRestorePassword,
        "X-ExportDocManager-Migration-File-Name": migrationRestoreFile?.name ?? "migration.edmmigration",
        "X-ExportDocManager-Restore-Confirmation": migrationRestoreConfirmation.trim(),
      }, { signal });
    }),
    onSuccess: async (response) => { setMigrationAdminPassword(""); setMigrationRestorePassword(""); setMigrationRestoreConfirmation(""); setMigrationRestoreFile(null); setMessage(null); setSuccessMessage(response.message || "服务器迁移已排队，请重启服务完成恢复。"); await queryClient.invalidateQueries({ queryKey: queryKeys.serverMigrationStatus() }); },
    onError: (error) => { setMigrationAdminPassword(""); setMigrationRestorePassword(""); setMigrationRestoreConfirmation(""); fail(error); },
  });

  const databaseRestoreMutation = useMutation({
    mutationFn: () => runAbortableOperation((signal) => client.restorePostgreSqlPhysicalBackup({ body: { backupFileName: selectedBackup, adminPassword: databaseAdminPassword, confirmationText: databaseRestoreConfirmation.trim() } }, { signal })),
    onSuccess: async (response) => { setDatabaseAdminPassword(""); setDatabaseRestoreConfirmation(""); setMessage(null); setSuccessMessage(response.message || "PostgreSQL 数据库恢复已排队，请重启服务完成恢复。"); await queryClient.invalidateQueries({ queryKey: queryKeys.serverMigrationStatus() }); },
    onError: (error) => { setDatabaseAdminPassword(""); setDatabaseRestoreConfirmation(""); fail(error); },
  });

  const downloadDatabaseMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => { const ticket = await client.createPostgreSqlPhysicalBackupDownloadTicket({ fileName: selectedBackup }, { signal }); startDownloadFromTicket(client, ticket, selectedBackup || "exportdoc-postgresql.dump"); }),
    onSuccess: () => { setMessage(null); setSuccessMessage("PostgreSQL 备份已交给浏览器下载。"); },
    onError: fail,
  });

  const uploadDatabaseRestoreMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const authorization = await client.authorizeServerMigrationOperation({ body: { action: "restore-database", adminPassword: databaseAdminPassword } }, { signal });
      return client.uploadAndRestorePostgreSqlPhysicalBackup({
        body: databaseUploadFile as File,
        "X-ExportDocManager-Sensitive-Operation-Ticket": authorization.ticket,
        "X-ExportDocManager-PostgreSql-Backup-File-Name": databaseUploadFile?.name ?? "database.dump",
        "X-ExportDocManager-Restore-Confirmation": databaseRestoreConfirmation.trim(),
      }, { signal });
    }),
    onSuccess: async (response) => { setDatabaseAdminPassword(""); setDatabaseUploadFile(null); setDatabaseRestoreConfirmation(""); setMessage(null); setSuccessMessage(response.message || "上传的 PostgreSQL 数据库恢复已排队，请重启服务完成恢复。"); await queryClient.invalidateQueries({ queryKey: queryKeys.serverMigrationStatus() }); },
    onError: (error) => { setDatabaseAdminPassword(""); setDatabaseRestoreConfirmation(""); fail(error); },
  });

  const status = postgreSqlQuery.data?.status;
  const backups = postgreSqlQuery.data?.backups ?? [];
  const migrationStatus = migrationStatusQuery.data;
  const isBusy = postgreSqlQuery.isFetching || (!migrationStatus && migrationStatusQuery.isFetching) || createMutation.isPending || restorePlanMutation.isPending || downloadMigrationMutation.isPending || restoreMigrationMutation.isPending || databaseRestoreMutation.isPending || downloadDatabaseMutation.isPending || uploadDatabaseRestoreMutation.isPending;
  const canCreate = canManageSettings && Boolean(status?.postgreSqlConfigured) && Boolean(status?.toolsReady) && !isBusy;
  const canCreatePlan = canManageSettings && Boolean(status?.postgreSqlConfigured) && Boolean(status?.toolsReady) && Boolean(selectedBackup) && Boolean(targetDatabase.trim()) && Boolean(applicationRole.trim()) && !isBusy;
  const canCreateMigration = canManageSettings && Boolean(migrationStatus?.supported) && Boolean(migrationStatus?.toolsReady) && !migrationStatus?.pendingRestore && Boolean(migrationAdminPassword) && migrationCreateConfirmation.trim() === "MIGRATE" && isStrongMigrationPassword(migrationPassword) && migrationPassword === migrationPasswordConfirmation && !isBusy;
  const canRestoreMigration = canManageSettings && Boolean(migrationStatus?.supported) && Boolean(migrationStatus?.toolsReady) && !migrationStatus?.pendingRestore && Boolean(migrationRestoreFile) && isStrongMigrationPassword(migrationRestorePassword) && Boolean(migrationAdminPassword) && migrationRestoreConfirmation.trim() === "MIGRATE" && !isBusy;
  const canRestoreDatabase = canManageSettings && Boolean(status?.postgreSqlConfigured) && Boolean(status?.toolsReady) && !migrationStatus?.pendingRestore && Boolean(selectedBackup) && Boolean(databaseAdminPassword) && databaseRestoreConfirmation.trim() === "RESTORE DATABASE" && !isBusy;
  const canUploadDatabase = canManageSettings && Boolean(status?.postgreSqlConfigured) && Boolean(status?.toolsReady) && !migrationStatus?.pendingRestore && Boolean(databaseUploadFile) && Boolean(databaseAdminPassword) && databaseRestoreConfirmation.trim() === "RESTORE DATABASE" && !isBusy;

  return (
    <section className="form-section backup-management-section" aria-label="PostgreSQL 团队库维护">
      <div className="section-header"><div><h2>PostgreSQL 团队库维护</h2><p className="section-description">按“备份、恢复、完整迁移、任务状态”分区，低频危险操作不会挤在同一张表单中。</p></div></div>
      {message ? <InlineNotice tone="error" title="数据库维护失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <MaintenanceBackgroundTaskStatusPanel status={migrationStatus} />
      <MaintenanceDatabaseBackupPanel
        canManageSettings={canManageSettings}
        busy={isBusy}
        status={status}
        backups={backups}
        isLoading={postgreSqlQuery.isFetching}
        canCreate={canCreate}
        createPending={createMutation.isPending}
        onRefresh={() => {
          clearFeedback();
          void postgreSqlQuery.refetch();
        }}
        onCreate={() => {
          clearFeedback();
          createMutation.mutate();
        }}
        onPathError={onPathError}
      />
      <MaintenanceDatabaseRecoveryPanel
        canManageSettings={canManageSettings}
        busy={isBusy}
        backups={backups}
        selectedBackup={selectedBackup}
        targetDatabase={targetDatabase}
        applicationRole={applicationRole}
        oldOwnerRoles={oldOwnerRoles}
        adminPassword={databaseAdminPassword}
        restoreConfirmation={databaseRestoreConfirmation}
        uploadFile={databaseUploadFile}
        canRestoreSelected={canRestoreDatabase}
        canRestoreUploaded={canUploadDatabase}
        canCreatePlan={canCreatePlan}
        onSelectedBackupChange={(value) => {
          setSelectedBackup(value);
          setDatabaseAdminPassword("");
          setDatabaseRestoreConfirmation("");
        }}
        onTargetDatabaseChange={setTargetDatabase}
        onApplicationRoleChange={setApplicationRole}
        onOldOwnerRolesChange={setOldOwnerRoles}
        onAdminPasswordChange={setDatabaseAdminPassword}
        onRestoreConfirmationChange={setDatabaseRestoreConfirmation}
        onUploadFileChange={(file) => {
          setDatabaseUploadFile(file);
          setDatabaseAdminPassword("");
          setDatabaseRestoreConfirmation("");
        }}
        onDownload={() => downloadDatabaseMutation.mutate()}
        onRestoreSelected={() => databaseRestoreMutation.mutate()}
        onRestoreUploaded={() => uploadDatabaseRestoreMutation.mutate()}
        onCreatePlan={() => {
          clearFeedback();
          restorePlanMutation.mutate();
        }}
      />
      <MaintenanceFullMigrationPanel
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
        onRestoreFileChange={(file) => {
          setMigrationRestoreFile(file);
          setMigrationAdminPassword("");
          setMigrationRestorePassword("");
          setMigrationRestoreConfirmation("");
        }}
        onRestorePasswordChange={setMigrationRestorePassword}
        onRestoreConfirmationChange={setMigrationRestoreConfirmation}
        onCreate={() => downloadMigrationMutation.mutate()}
        onRestore={() => restoreMigrationMutation.mutate()}
      />
      {lastRestorePlanPath ? <InlineNotice tone="info" action={renderOpenPathAction(lastRestorePlanPath, "打开还原计划目录", onPathError)}>{lastRestorePlanPath}</InlineNotice> : null}
    </section>
  );
}
