import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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

export function useBackupManagement(client: ExportDocManagerApiClient, canManageSettings: boolean) {
  const queryClient = useQueryClient();
  const requestConfirmation = useConfirmation();
  const desktopBridgeAvailable = isDesktopBridgeAvailable();
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const [cleanupDays, setCleanupDays] = useState(30);
  const [restoreFileName, setRestoreFileName] = useState("");
  const [restoreConfirmation, setRestoreConfirmation] = useState("");
  const [cloudDownloadFileName, setCloudDownloadFileName] = useState("");
  const [runtimeStorage, setRuntimeStorage] = useState<RuntimeStorageContext | null>(null);
  const [dataRootMigrationPending, setDataRootMigrationPending] = useState(false);
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
  }, [desktopBridgeAvailable]);

  const backupQuery = useQuery({
    queryKey: queryKeys.backups(),
    queryFn: ({ signal }) => client.listDatabaseBackups({ signal }),
    enabled: canManageSettings,
  });
  const cloudStatusQuery = useQuery({
    queryKey: queryKeys.cloudBackupStatus(),
    queryFn: ({ signal }) => client.getCloudBackupStatus({ signal }),
    enabled: canManageSettings,
  });
  const cloudBackupsQuery = useQuery({
    queryKey: queryKeys.cloudBackupBackups(),
    queryFn: ({ signal }) => client.listCloudDatabaseBackups({ signal }),
    enabled: canManageSettings && Boolean(cloudStatusQuery.data?.enabled) && Boolean(cloudStatusQuery.data?.isConfigured),
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
    const remoteBackups = cloudBackupsQuery.data?.backups ?? [];
    if (remoteBackups.length === 0) {
      setCloudDownloadFileName("");
      return;
    }
    if (!remoteBackups.some((backup) => backup.fileName === cloudDownloadFileName)) {
      setCloudDownloadFileName(remoteBackups[0].fileName);
    }
  }, [cloudBackupsQuery.data, cloudDownloadFileName]);

  useEffect(() => {
    const failedQuery = [backupQuery, cloudStatusQuery, cloudBackupsQuery, disasterRecoveryQuery]
      .find((query) => query.isError);
    if (failedQuery) {
      setMessage(readApiError(failedQuery.error));
      setSuccessMessage(null);
    }
  }, [
    backupQuery.error,
    backupQuery.isError,
    cloudBackupsQuery.error,
    cloudBackupsQuery.isError,
    cloudStatusQuery.error,
    cloudStatusQuery.isError,
    disasterRecoveryQuery.error,
    disasterRecoveryQuery.isError,
  ]);

  const mutationError = (error: unknown) => {
    setMessage(readApiError(error));
    setSuccessMessage(null);
  };
  const createMutation = useMutation({
    mutationFn: async () => {
      const acceptedJob = await client.createDatabaseBackup();
      return waitForJobCompletion(client, acceptedJob, {
        timeoutMessage: "数据库备份仍在后台生成，可稍后到任务中心查看结果。",
      });
    },
    onSuccess: (job) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.backups() });
      setMessage(null);
      setSuccessMessage("数据库备份已完成，备份列表正在刷新。");
      setLastCreatedJobId(job.jobId);
    },
    onError: mutationError,
  });
  const cleanupMutation = useMutation({
    mutationFn: () => client.cleanupDatabaseBackups({ body: { daysToKeep: cleanupDays } }),
    onSuccess: (response) => {
      updateBackupQuery(queryClient, response);
      setMessage(null);
      setSuccessMessage(response.message || "旧备份清理完成。");
    },
    onError: mutationError,
  });
  const uploadCloudMutation = useMutation({
    mutationFn: () => client.uploadLatestDatabaseBackupToCloud(),
    onSuccess: (job) => {
      setMessage(null);
      setSuccessMessage("WebDAV 上传任务已加入任务中心。");
      setLastCreatedJobId(job.jobId);
    },
    onError: mutationError,
  });
  const downloadCloudMutation = useMutation({
    mutationFn: () => client.downloadCloudDatabaseBackup({ body: { remoteFileName: cloudDownloadFileName } }),
    onSuccess: (job) => {
      setMessage(null);
      setSuccessMessage("WebDAV 下载与校验任务已加入任务中心；完成后刷新备份列表即可使用。");
      setLastCreatedJobId(job.jobId);
    },
    onError: mutationError,
  });
  const restoreMutation = useMutation({
    mutationFn: () => client.restoreDatabaseBackup({
      body: { backupFileName: restoreFileName, confirmationText: restoreConfirmation.trim() },
    }),
    onSuccess: (job) => {
      setMessage(null);
      setSuccessMessage("数据库还原校验任务已加入任务中心；任务成功后请立即重启程序完成离线还原。");
      setLastCreatedJobId(job.jobId);
      setRestoreConfirmation("");
    },
    onError: mutationError,
  });
  const createRecoveryMutation = useMutation({
    mutationFn: () => client.createDisasterRecoveryPackage({ body: { password: recoveryPassword } }),
    onSuccess: (job) => {
      setMessage(null);
      setSuccessMessage("加密灾难恢复包任务已加入任务中心；完成后可在恢复包目录中查看。");
      setLastCreatedJobId(job.jobId);
      setRecoveryPassword("");
      setRecoveryPasswordConfirmation("");
    },
    onError: mutationError,
  });
  const restoreRecoveryMutation = useMutation({
    mutationFn: () => client.restoreDisasterRecoveryPackage({
      body: {
        packagePath: recoveryPackagePath,
        password: recoveryRestorePassword,
        confirmationText: recoveryRestoreConfirmation.trim(),
      },
    }),
    onSuccess: (job) => {
      setMessage(null);
      setSuccessMessage("灾难恢复校验任务已加入任务中心；任务成功后请立即重启程序。");
      setLastCreatedJobId(job.jobId);
      setRecoveryRestorePassword("");
      setRecoveryRestoreConfirmation("");
    },
    onError: mutationError,
  });

  const backups = backupQuery.data?.backups ?? [];
  const cloudStatus = cloudStatusQuery.data ?? null;
  const cloudBackups = cloudBackupsQuery.data?.backups ?? [];
  const disasterRecoveryStatus = disasterRecoveryQuery.data ?? null;
  const cloudBackupsEnabled = canManageSettings && Boolean(cloudStatus?.enabled) && Boolean(cloudStatus?.isConfigured);
  const isBusy =
    backupQuery.isFetching || cloudStatusQuery.isFetching || cloudBackupsQuery.isFetching || disasterRecoveryQuery.isFetching ||
    createMutation.isPending || cleanupMutation.isPending || uploadCloudMutation.isPending || downloadCloudMutation.isPending ||
    restoreMutation.isPending || createRecoveryMutation.isPending || restoreRecoveryMutation.isPending || dataRootMigrationPending;

  const clearNotices = () => {
    setMessage(null);
    setSuccessMessage(null);
    setLastCreatedJobId(null);
  };
  const runMutation = (mutate: () => void) => {
    clearNotices();
    mutate();
  };
  const refreshBackups = () => {
    clearNotices();
    void backupQuery.refetch();
    void cloudStatusQuery.refetch();
    if (desktopBridgeAvailable) void disasterRecoveryQuery.refetch();
    if (cloudBackupsEnabled) void cloudBackupsQuery.refetch();
  };
  const chooseNewDataRoot = async () => {
    if (!runtimeStorage?.migrationSupported || dataRootMigrationPending) return;
    if (!await requestConfirmation({
      title: "更换运行数据目录",
      description: "程序将在退出后迁移现有数据，并于下次启动使用新目录。",
      details: ["请选择本机磁盘上的保存位置，程序会创建 ExportDocManager_Data 专用子目录。", "迁移期间请勿关闭计算机或拔出目标磁盘。"],
      confirmLabel: "继续选择目录",
      tone: "warning",
    })) return;

    setDataRootMigrationPending(true);
    clearNotices();
    try {
      const result = await scheduleDataRootMigration();
      if (!result) return;
      setSuccessMessage(`${result.message} 新目录：${result.targetDataRoot}`);
      const context = await getRuntimeStorageContext();
      if (context) setRuntimeStorage(context);
    } catch (error) {
      setMessage(readDesktopError(error));
    } finally {
      setDataRootMigrationPending(false);
    }
  };
  const chooseRecoveryPackage = async () => {
    clearNotices();
    try {
      const selected = await selectDisasterRecoveryPackageFile();
      if (selected) {
        setRecoveryPackagePath(selected);
        setRecoveryRestoreConfirmation("");
      }
    } catch (error) {
      setMessage(readDesktopError(error));
    }
  };

  return {
    canManageSettings,
    message,
    successMessage,
    lastCreatedJobId,
    desktopBridgeAvailable,
    runtimeStorage,
    dataRootMigrationPending,
    backupRoot: backupQuery.data?.backupRoot,
    backupLoading: backupQuery.isFetching,
    backups,
    cloudStatus,
    cloudBackups,
    disasterRecoveryStatus,
    isBusy,
    cleanupDays,
    setCleanupDays,
    restoreFileName,
    setRestoreFileName,
    restoreConfirmation,
    setRestoreConfirmation,
    cloudDownloadFileName,
    setCloudDownloadFileName,
    recoveryPassword,
    setRecoveryPassword,
    recoveryPasswordConfirmation,
    setRecoveryPasswordConfirmation,
    recoveryPackagePath,
    recoveryRestorePassword,
    setRecoveryRestorePassword,
    recoveryRestoreConfirmation,
    setRecoveryRestoreConfirmation,
    cloudBackupsEnabled,
    canRestore: canManageSettings && Boolean(restoreFileName) && restoreConfirmation.trim() === "RESTORE" && !isBusy,
    canUploadCloud: canManageSettings && Boolean(cloudStatus?.enabled) && Boolean(cloudStatus?.isConfigured) && backups.length > 0 && !isBusy,
    canDownloadCloud: cloudBackupsEnabled && Boolean(cloudDownloadFileName) && cloudBackups.length > 0 && !isBusy,
    canCreateRecovery: canManageSettings && Boolean(disasterRecoveryStatus?.supported) && !disasterRecoveryStatus?.pendingRestore &&
      isStrongRecoveryPassword(recoveryPassword) && recoveryPassword === recoveryPasswordConfirmation && !isBusy,
    canRestoreRecovery: canManageSettings && Boolean(disasterRecoveryStatus?.supported) && !disasterRecoveryStatus?.pendingRestore &&
      Boolean(recoveryPackagePath) && isStrongRecoveryPassword(recoveryRestorePassword) &&
      recoveryRestoreConfirmation.trim() === "RECOVER" && !isBusy,
    refreshBackups,
    chooseNewDataRoot,
    chooseRecoveryPackage,
    createBackup: () => runMutation(createMutation.mutate),
    cleanupBackups: () => runMutation(cleanupMutation.mutate),
    uploadLatestBackup: () => runMutation(uploadCloudMutation.mutate),
    downloadCloudBackup: () => runMutation(downloadCloudMutation.mutate),
    restoreBackup: () => runMutation(restoreMutation.mutate),
    createRecoveryPackage: () => runMutation(createRecoveryMutation.mutate),
    restoreRecoveryPackage: () => runMutation(restoreRecoveryMutation.mutate),
  };
}

export type BackupManagementController = ReturnType<typeof useBackupManagement>;
