import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { encodeHeaderText } from "../../api/headerText.ts";
import {
  downloadJobResultWhenReady,
  startDownloadFromTicket,
  waitForJobCompletion,
} from "../../ui/downloadJobResult.ts";
import { currentLocalDateInputValue, readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { parseStringArray } from "./settingsValueUtils.ts";

function isStrongMigrationPassword(value: string) {
  return (
    value.length >= 12 &&
    value.length <= 128 &&
    /[A-Z]/.test(value) &&
    /[a-z]/.test(value) &&
    /\d/.test(value) &&
    /[^A-Za-z0-9]/.test(value)
  );
}

export function usePostgreSqlMaintenance(
  client: ExportDocManagerApiClient,
  canManageSettings: boolean,
  view: "migration" | "recovery" | "all",
) {
  const scope = `postgres-maintenance:${view}:${canManageSettings}`;
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
  const databaseDetails = useRef<HTMLDetailsElement>(null);
  const migrationDetails = useRef<HTMLDetailsElement>(null);

  const postgreSqlQuery = useQuery({
    queryKey: queryKeys.postgreSqlMaintenanceBackups(),
    queryFn: ({ signal }) => client.listPostgreSqlPhysicalBackups({ signal }),
    enabled: canManageSettings,
  });
  const migrationStatusQuery = useQuery({
    queryKey: queryKeys.serverMigrationStatus(),
    queryFn: ({ signal }) => client.getServerMigrationStatus({ signal }),
    enabled: canManageSettings,
    refetchInterval: (query) => (canManageSettings && query.state.data?.pendingRestore ? 5_000 : false),
  });

  useEffect(() => {
    setDatabaseAdminPassword("");
    setDatabaseRestoreConfirmation("");
    setDatabaseUploadFile(null);
    setMigrationAdminPassword("");
    setMigrationPassword("");
    setMigrationPasswordConfirmation("");
    setMigrationCreateConfirmation("");
    setMigrationRestoreFile(null);
    setMigrationRestorePassword("");
    setMigrationRestoreConfirmation("");
    setMessage(null);
    setSuccessMessage(null);
    setLastRestorePlanPath("");
  }, [client, canManageSettings, view]);

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
    if (!selectedBackup || !backups.some((backup) => backup.fileName === selectedBackup))
      setSelectedBackup(backups[0].fileName);
  }, [postgreSqlQuery.data, selectedBackup]);

  useEffect(() => {
    if (postgreSqlQuery.isError) {
      setMessage(readApiError(postgreSqlQuery.error));
      setSuccessMessage(null);
    }
  }, [postgreSqlQuery.error, postgreSqlQuery.isError]);

  const fail = (error: unknown) => {
    setMessage(readApiError(error));
    setSuccessMessage(null);
  };
  const failDatabase = (error: unknown) => {
    if (databaseDetails.current) databaseDetails.current.open = true;
    fail(error);
  };
  const failMigration = (error: unknown) => {
    if (migrationDetails.current) migrationDetails.current.open = true;
    fail(error);
  };
  const clearFeedback = () => {
    setMessage(null);
    setSuccessMessage(null);
  };

  const createMutation = useScopedMutation(scope, client, {
    mutationFn: async (_input: void, signal) => {
      const job = await client.createPostgreSqlPhysicalBackup({ signal });
      return waitForJobCompletion(client, job, {
        timeoutMs: 60 * 60 * 1000,
        pollIntervalMs: 2_000,
        signal,
        timeoutMessage: "数据库备份仍在后台运行，可稍后到“文件任务”查看，完成后刷新备份列表。",
      });
    },
    onSuccess: () => {
      setSuccessMessage("PostgreSQL 数据库备份已创建，请在列表下载并另存。");
      setMessage(null);
    },
    refreshKeys: () => [queryKeys.postgreSqlMaintenanceBackups()],
    onError: fail,
  });

  const restorePlanMutation = useScopedMutation(scope, client, {
    mutationFn: (_input: void, signal) =>
      client.createPostgreSqlRestorePlan(
        {
          body: {
            backupFileName: selectedBackup,
            targetDatabase: targetDatabase.trim(),
            applicationRole: applicationRole.trim(),
            oldOwnerRoles: parseStringArray(oldOwnerRoles),
          },
        },
        { signal },
      ),
    onSuccess: (response) => {
      setLastRestorePlanPath(response.planRoot);
      setMessage(null);
      setSuccessMessage(response.message || "PostgreSQL 还原与权限改派脚本已生成。");
    },
    onError: failDatabase,
  });

  const downloadMigrationMutation = useScopedMutation(scope, client, {
    mutationFn: async (_input: void, signal) => {
      const job = await client.createServerMigrationPackage(
        {
          body: {
            password: migrationPassword,
            adminPassword: migrationAdminPassword,
            confirmationText: migrationCreateConfirmation.trim(),
          },
        },
        { signal },
      );
      return downloadJobResultWhenReady(
        client,
        job,
        `export-doc-manager-server-migration-${currentLocalDateInputValue()}.edmmigration`,
        { timeoutMs: 60 * 60 * 1000, pollIntervalMs: 2_000, signal },
      );
    },
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
      setMigrationCreateConfirmation("");
      failMigration(error);
    },
  });

  const restoreMigrationMutation = useScopedMutation(scope, client, {
    mutationFn: async (_input: void, signal) => {
      const authorization = await client.authorizeServerMigrationOperation(
        {
          body: {
            action: "restore-server",
            adminPassword: migrationAdminPassword,
          },
        },
        { signal },
      );
      signal.throwIfAborted();
      return client.stageServerMigrationRestore(
        {
          body: migrationRestoreFile as File,
          "X-ExportDocManager-Sensitive-Operation-Ticket": authorization.ticket,
          "X-ExportDocManager-Migration-Password": encodeHeaderText(migrationRestorePassword),
          "X-ExportDocManager-Migration-File-Name": encodeHeaderText(
            migrationRestoreFile?.name ?? "migration.edmmigration",
          ),
          "X-ExportDocManager-Restore-Confirmation": migrationRestoreConfirmation.trim(),
        },
        { signal, timeoutMs: 24 * 60 * 60 * 1000 },
      );
    },
    onSuccess: (response) => {
      setMigrationAdminPassword("");
      setMigrationRestorePassword("");
      setMigrationRestoreConfirmation("");
      setMigrationRestoreFile(null);
      setMessage(null);
      setSuccessMessage(response.message || "服务器迁移已暂存，请部署管理员停止应用并执行恢复维护命令。");
    },
    refreshKeys: () => [queryKeys.serverMigrationStatus()],
    onError: (error) => {
      setMigrationAdminPassword("");
      setMigrationRestorePassword("");
      setMigrationRestoreConfirmation("");
      failMigration(error);
    },
  });

  const databaseRestoreMutation = useScopedMutation(scope, client, {
    mutationFn: (_input: void, signal) =>
      client.restorePostgreSqlPhysicalBackup(
        {
          body: {
            backupFileName: selectedBackup,
            adminPassword: databaseAdminPassword,
            confirmationText: databaseRestoreConfirmation.trim(),
          },
        },
        { signal, timeoutMs: 24 * 60 * 60 * 1000 },
      ),
    onSuccess: (response) => {
      setDatabaseAdminPassword("");
      setDatabaseRestoreConfirmation("");
      setMessage(null);
      setSuccessMessage(response.message || "PostgreSQL 数据库恢复已暂存，请部署管理员停止应用并执行恢复维护命令。");
    },
    refreshKeys: () => [queryKeys.serverMigrationStatus()],
    onError: (error) => {
      setDatabaseAdminPassword("");
      setDatabaseRestoreConfirmation("");
      failDatabase(error);
    },
  });

  const downloadDatabaseMutation = useScopedMutation(scope, client, {
    mutationFn: async (fileName: string, signal) => {
      const ticket = await client.createPostgreSqlPhysicalBackupDownloadTicket({ fileName }, { signal });
      signal.throwIfAborted();
      startDownloadFromTicket(client, ticket, fileName);
    },
    onSuccess: () => {
      setMessage(null);
      setSuccessMessage("PostgreSQL 备份已交给浏览器下载。");
    },
    onError: fail,
  });

  const uploadDatabaseRestoreMutation = useScopedMutation(scope, client, {
    mutationFn: async (_input: void, signal) => {
      const authorization = await client.authorizeServerMigrationOperation(
        {
          body: {
            action: "restore-database",
            adminPassword: databaseAdminPassword,
          },
        },
        { signal },
      );
      signal.throwIfAborted();
      return client.uploadAndRestorePostgreSqlPhysicalBackup(
        {
          body: databaseUploadFile as File,
          "X-ExportDocManager-Sensitive-Operation-Ticket": authorization.ticket,
          "X-ExportDocManager-PostgreSql-Backup-File-Name": encodeHeaderText(
            databaseUploadFile?.name ?? "database.dump",
          ),
          "X-ExportDocManager-Restore-Confirmation": databaseRestoreConfirmation.trim(),
        },
        { signal, timeoutMs: 24 * 60 * 60 * 1000 },
      );
    },
    onSuccess: (response) => {
      setDatabaseAdminPassword("");
      setDatabaseUploadFile(null);
      setDatabaseRestoreConfirmation("");
      setMessage(null);
      setSuccessMessage(
        response.message || "上传的 PostgreSQL 数据库恢复已暂存，请部署管理员停止应用并执行恢复维护命令。",
      );
    },
    refreshKeys: () => [queryKeys.serverMigrationStatus()],
    onError: (error) => {
      setDatabaseAdminPassword("");
      setDatabaseRestoreConfirmation("");
      failDatabase(error);
    },
  });

  const status = postgreSqlQuery.data?.status;
  const backups = postgreSqlQuery.data?.backups ?? [];
  const migrationStatus = migrationStatusQuery.data;
  const isBusy =
    postgreSqlQuery.isFetching ||
    (!migrationStatus && migrationStatusQuery.isFetching) ||
    createMutation.isPending ||
    restorePlanMutation.isPending ||
    downloadMigrationMutation.isPending ||
    restoreMigrationMutation.isPending ||
    databaseRestoreMutation.isPending ||
    downloadDatabaseMutation.isPending ||
    uploadDatabaseRestoreMutation.isPending;
  const canCreate =
    canManageSettings &&
    Boolean(status?.postgreSqlConfigured) &&
    Boolean(status?.toolsReady) &&
    !migrationStatus?.pendingRestore &&
    !isBusy;
  const canCreatePlan =
    canManageSettings &&
    Boolean(status?.postgreSqlConfigured) &&
    Boolean(status?.toolsReady) &&
    Boolean(selectedBackup) &&
    Boolean(targetDatabase.trim()) &&
    Boolean(applicationRole.trim()) &&
    !isBusy;
  const canCreateMigration =
    canManageSettings &&
    Boolean(migrationStatus?.supported) &&
    Boolean(migrationStatus?.toolsReady) &&
    !migrationStatus?.pendingRestore &&
    Boolean(migrationAdminPassword) &&
    migrationCreateConfirmation.trim() === "MIGRATE" &&
    isStrongMigrationPassword(migrationPassword) &&
    migrationPassword === migrationPasswordConfirmation &&
    !isBusy;
  const canRestoreMigration =
    canManageSettings &&
    Boolean(migrationStatus?.supported) &&
    Boolean(migrationStatus?.toolsReady) &&
    !migrationStatus?.pendingRestore &&
    Boolean(migrationRestoreFile) &&
    Boolean(migrationRestorePassword) &&
    Boolean(migrationAdminPassword) &&
    migrationRestoreConfirmation.trim() === "MIGRATE" &&
    !isBusy;
  const canRestoreDatabase =
    canManageSettings &&
    Boolean(status?.postgreSqlConfigured) &&
    Boolean(status?.toolsReady) &&
    !migrationStatus?.pendingRestore &&
    Boolean(selectedBackup) &&
    Boolean(databaseAdminPassword) &&
    databaseRestoreConfirmation.trim() === "RESTORE DATABASE" &&
    !isBusy;
  const canUploadDatabase =
    canManageSettings &&
    Boolean(status?.postgreSqlConfigured) &&
    Boolean(status?.toolsReady) &&
    !migrationStatus?.pendingRestore &&
    Boolean(databaseUploadFile) &&
    Boolean(databaseAdminPassword) &&
    databaseRestoreConfirmation.trim() === "RESTORE DATABASE" &&
    !isBusy;

  return {
    message,
    successMessage,
    lastRestorePlanPath,
    databaseDetails,
    migrationDetails,
    postgreSqlQuery,
    status,
    backups,
    migrationStatus,
    isBusy,
    canCreate,
    canCreatePlan,
    canCreateMigration,
    canRestoreMigration,
    canRestoreDatabase,
    canUploadDatabase,
    createMutation,
    restorePlanMutation,
    downloadMigrationMutation,
    restoreMigrationMutation,
    databaseRestoreMutation,
    downloadDatabaseMutation,
    uploadDatabaseRestoreMutation,
    clearFeedback,
    selectedBackup,
    targetDatabase,
    applicationRole,
    oldOwnerRoles,
    databaseAdminPassword,
    databaseRestoreConfirmation,
    databaseUploadFile,
    migrationAdminPassword,
    migrationPassword,
    migrationPasswordConfirmation,
    migrationCreateConfirmation,
    migrationRestoreFile,
    migrationRestorePassword,
    migrationRestoreConfirmation,
    setSelectedBackup,
    setTargetDatabase,
    setApplicationRole,
    setOldOwnerRoles,
    setDatabaseAdminPassword,
    setDatabaseRestoreConfirmation,
    setDatabaseUploadFile,
    setMigrationAdminPassword,
    setMigrationPassword,
    setMigrationPasswordConfirmation,
    setMigrationCreateConfirmation,
    setMigrationRestoreFile,
    setMigrationRestorePassword,
    setMigrationRestoreConfirmation,
  };
}
