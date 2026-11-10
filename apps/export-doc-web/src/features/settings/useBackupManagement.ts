import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
  getRuntimeStorageContext,
  isDesktopBridgeAvailable,
  scheduleDataRootMigration,
  selectDisasterRecoveryPackageFile,
  type RuntimeStorageContext,
} from "../../desktop/desktopBridge.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { waitForJobCompletion } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { isStrongRecoveryPassword, readDesktopError, updateBackupQuery } from "./backupManagementModel.ts";

export function useBackupManagement(client: ExportDocManagerApiClient, canManageSettings: boolean, sections = { database: true }) {
  const queryClient = useQueryClient();
  const requestConfirmation = useConfirmation();
  const desktopBridgeAvailable = isDesktopBridgeAvailable();
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const [cleanupDays, setCleanupDays] = useState(30);
  const [restoreFileName, setRestoreFileName] = useState("");
  const [restoreConfirmation, setRestoreConfirmation] = useState("");
  const [runtimeStorage, setRuntimeStorage] = useState<RuntimeStorageContext | null>(null);
  const [recoveryPassword, setRecoveryPassword] = useState("");
  const [recoveryPasswordConfirmation, setRecoveryPasswordConfirmation] = useState("");
  const [recoveryPackagePath, setRecoveryPackagePath] = useState("");
  const [recoveryRestorePassword, setRecoveryRestorePassword] = useState("");
  const [recoveryRestoreConfirmation, setRecoveryRestoreConfirmation] = useState("");

  useEffect(() => {
    if (!desktopBridgeAvailable) {
      return;
    }

    let cancelled = false;
    void getRuntimeStorageContext()
      .then((context) => {
        if (!cancelled) {
          setRuntimeStorage(context);
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setMessage(readDesktopError(error));
        }
      });
    return () => {
      cancelled = true;
    };
  }, [desktopBridgeAvailable, client]);

  const backupQuery = useQuery({
    queryKey: queryKeys.backups(),
    queryFn: ({ signal }) => client.listDatabaseBackups({ signal }),
    enabled: canManageSettings && sections.database,
  });
  const disasterRecoveryQuery = useQuery({
    queryKey: queryKeys.disasterRecoveryStatus(),
    queryFn: ({ signal }) => client.getDisasterRecoveryStatus({ signal }),
    enabled: canManageSettings && desktopBridgeAvailable,
  });

  useEffect(() => {
    const backups = backupQuery.data?.backups ?? [];
    if (backups.length === 0) {
      if (restoreFileName) {
        setRestoreFileName("");
        setRestoreConfirmation("");
      }
      return;
    }
    if (!restoreFileName || !backups.some((backup) => backup.fileName === restoreFileName)) {
      setRestoreFileName(backups[0].fileName);
      setRestoreConfirmation("");
    }
  }, [backupQuery.data, restoreFileName]);

  useEffect(() => {
    const failedQuery = [backupQuery, disasterRecoveryQuery]
      .find((query) => query.isError);
    if (failedQuery) {
      setMessage(readApiError(failedQuery.error));
      setSuccessMessage(null);
    }
  }, [
    backupQuery.error,
    backupQuery.isError,
    disasterRecoveryQuery.error,
    disasterRecoveryQuery.isError,
  ]);

  const mutationError = (error: unknown) => {
    setMessage(readApiError(error));
    setSuccessMessage(null);
  };
  const createMutation = useScopedMutation("backup", client, {
    mutationFn: async (_: void, signal) => {
      const acceptedJob = await client.createDatabaseBackup({ signal });
      return waitForJobCompletion(client, acceptedJob, {
        signal,
        timeoutMessage: "数据库备份仍在后台生成，可稍后到“文件任务”查看结果。",
      });
    },
    onSuccess: (job) => {
      setMessage(null);
      setSuccessMessage("数据库备份已完成，备份列表正在刷新。");
      setLastCreatedJobId(job.jobId);
    },
    refreshKeys: () => [queryKeys.backups()],
    onError: mutationError,
  });
  const cleanupMutation = useScopedMutation("backup", client, {
    mutationFn: (_: void, signal) => client.cleanupDatabaseBackups({ body: { daysToKeep: cleanupDays } }, { signal }),
    onSuccess: (response) => {
      updateBackupQuery(queryClient, response);
      setMessage(null);
      setSuccessMessage(response.message || "旧备份清理完成。");
    },
    onError: mutationError,
  });
  const restoreMutation = useScopedMutation("backup", client, {
    mutationFn: (_: void, signal) => client.restoreDatabaseBackup({
      body: { backupFileName: restoreFileName, confirmationText: restoreConfirmation.trim() },
    }, { signal }),
    onSuccess: (job) => {
      setMessage(null);
      setSuccessMessage("数据库已恢复，请重新登录。");
      void client.getCurrentUser().catch(() => undefined);
      setLastCreatedJobId(job.jobId);
      setRestoreConfirmation("");
    },
    onError: mutationError,
  });
  const createRecoveryMutation = useScopedMutation("backup", client, {
    mutationFn: (_: void, signal) => client.createDisasterRecoveryPackage({ body: { password: recoveryPassword } }, { signal }),
    onSuccess: (job) => {
      setMessage(null);
      setSuccessMessage("加密灾难恢复包任务已加入“文件任务”；完成后可在恢复包目录中查看。");
      setLastCreatedJobId(job.jobId);
      setRecoveryPassword("");
      setRecoveryPasswordConfirmation("");
    },
    onError: mutationError,
  });
  const restoreRecoveryMutation = useScopedMutation("backup", client, {
    mutationFn: (_: void, signal) => client.restoreDisasterRecoveryPackage({
      body: {
        packagePath: recoveryPackagePath,
        password: recoveryRestorePassword,
        confirmationText: recoveryRestoreConfirmation.trim(),
      },
    }, { signal }),
    onSuccess: (job) => {
      setMessage(null);
      setSuccessMessage("灾难恢复校验任务已加入“文件任务”；任务成功后请立即重启程序。");
      setLastCreatedJobId(job.jobId);
      setRecoveryRestorePassword("");
      setRecoveryRestoreConfirmation("");
    },
    onError: mutationError,
  });

  const migrationMutation = useScopedMutation("backup", client, {
    mutationFn: async (_: void, signal) => {
      if (!await requestConfirmation({
        title: "更换运行数据目录",
        description: "程序将在退出后迁移现有数据，并于下次启动使用新目录。",
        details: ["请选择本机磁盘上的保存位置，程序会创建 ExportDocManager_Data 专用子目录。", "迁移期间请勿关闭计算机或拔出目标磁盘。"],
        confirmLabel: "继续选择目录", tone: "warning",
      })) return null;
      signal.throwIfAborted();
      const result = await scheduleDataRootMigration();
      signal.throwIfAborted();
      if (!result) return null;
      return { result, context: await getRuntimeStorageContext() };
    },
    onSuccess: (saved) => {
      if (!saved) return;
      setSuccessMessage(`${saved.result.message} 新目录：${saved.result.targetDataRoot}`);
      if (saved.context) setRuntimeStorage(saved.context);
    },
    onError: error => setMessage(readDesktopError(error)),
  });
  const selectionMutation = useScopedMutation("backup", client, {
    mutationFn: () => selectDisasterRecoveryPackageFile(),
    onSuccess: selected => {
      if (selected) { setRecoveryPackagePath(selected); setRecoveryRestoreConfirmation(""); }
    },
    onError: error => setMessage(readDesktopError(error)),
  });
  const backups = backupQuery.data?.backups ?? [];
  const disasterRecoveryStatus = disasterRecoveryQuery.data ?? null;
  const isBusy =
    backupQuery.isFetching || disasterRecoveryQuery.isFetching ||
    createMutation.isPending || cleanupMutation.isPending ||
    restoreMutation.isPending || createRecoveryMutation.isPending || restoreRecoveryMutation.isPending || migrationMutation.isPending || selectionMutation.isPending;

  const clearNotices = () => {
    setMessage(null);
    setSuccessMessage(null);
    setLastCreatedJobId(null);
  };
  const runMutation = (mutate: () => void) => {
    if (isBusy || !canManageSettings) return;
    clearNotices();
    mutate();
  };
  const refreshBackups = () => {
    clearNotices();
    if (sections.database) void backupQuery.refetch();
    if (desktopBridgeAvailable) void disasterRecoveryQuery.refetch();
  };
  const chooseNewDataRoot = () => { if (runtimeStorage?.migrationSupported) runMutation(migrationMutation.mutate); };
  const chooseRecoveryPackage = () => runMutation(selectionMutation.mutate);

  return {
    canManageSettings,
    message,
    successMessage,
    lastCreatedJobId,
    desktopBridgeAvailable,
    runtimeStorage,
    backupRoot: backupQuery.data?.backupRoot,
    backupLoading: backupQuery.isFetching,
    backups,
    disasterRecoveryStatus,
    isBusy,
    cleanupDays,
    setCleanupDays,
    restoreFileName,
    setRestoreFileName,
    restoreConfirmation,
    setRestoreConfirmation,
    recoveryPassword,
    setRecoveryPassword,
    recoveryPasswordConfirmation,
    setRecoveryPasswordConfirmation,
    recoveryPackagePath,
    recoveryRestorePassword,
    setRecoveryRestorePassword,
    recoveryRestoreConfirmation,
    setRecoveryRestoreConfirmation,
    canRestore: canManageSettings && Boolean(restoreFileName) && restoreConfirmation.trim() === "RESTORE" && !isBusy,
    canCreateRecovery: canManageSettings && Boolean(disasterRecoveryStatus?.supported) && !disasterRecoveryStatus?.pendingRestore &&
      isStrongRecoveryPassword(recoveryPassword) && recoveryPassword === recoveryPasswordConfirmation && !isBusy,
    canRestoreRecovery: canManageSettings && Boolean(disasterRecoveryStatus?.supported) && !disasterRecoveryStatus?.pendingRestore &&
      Boolean(recoveryPackagePath) && Boolean(recoveryRestorePassword) &&
      recoveryRestoreConfirmation.trim() === "RECOVER" && !isBusy,
    refreshBackups,
    chooseNewDataRoot,
    chooseRecoveryPackage,
    createBackup: () => runMutation(createMutation.mutate),
    cleanupBackups: () => runMutation(cleanupMutation.mutate),
    restoreBackup: () => runMutation(restoreMutation.mutate),
    createRecoveryPackage: () => runMutation(createRecoveryMutation.mutate),
    restoreRecoveryPackage: () => runMutation(restoreRecoveryMutation.mutate),
  };
}

export type BackupManagementController = ReturnType<typeof useBackupManagement>;
