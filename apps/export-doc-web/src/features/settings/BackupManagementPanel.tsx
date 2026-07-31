import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, CloudDownload, CloudUpload, HardDrive, RefreshCw, RotateCcw, ShieldCheck, Trash2 } from "lucide-react";
import type {
  ApiBackupCreateResponse,
  ApiBackupListResponse,
  ApiCloudBackupStatusResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
  getRuntimeStorageContext,
  isDesktopBridgeAvailable,
  scheduleDataRootMigration,
  selectDisasterRecoveryPackageFile,
  type RuntimeStorageContext,
} from "../../desktop/desktopBridge.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { NumberField, SelectField } from "../../ui/FormFields.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";

export default function BackupManagementPanel({
  client,
  canManageSettings,
  onPathError,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  onPathError: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const requestConfirmation = useConfirmation();
  const desktopBridgeAvailable = isDesktopBridgeAvailable();
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
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
  const [lastRecoveryPackagePath, setLastRecoveryPackagePath] = useState("");

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
    queryFn: () => client.listDatabaseBackups(),
    enabled: canManageSettings,
  });

  const cloudStatusQuery = useQuery({
    queryKey: queryKeys.cloudBackupStatus(),
    queryFn: () => client.getCloudBackupStatus(),
    enabled: canManageSettings,
  });

  const cloudBackupsQuery = useQuery({
    queryKey: queryKeys.cloudBackupBackups(),
    queryFn: () => client.listCloudDatabaseBackups(),
    enabled: canManageSettings && Boolean(cloudStatusQuery.data?.enabled) && Boolean(cloudStatusQuery.data?.isConfigured),
  });

  const disasterRecoveryQuery = useQuery({
    queryKey: queryKeys.disasterRecoveryStatus(),
    queryFn: () => client.getDisasterRecoveryStatus(),
    enabled: canManageSettings && desktopBridgeAvailable,
  });

  useEffect(() => {
    if (backupQuery.data?.backups.length && !restoreFileName) {
      setRestoreFileName(backupQuery.data.backups[0].fileName);
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
    if (backupQuery.isError) {
      setMessage(readApiError(backupQuery.error));
      setSuccessMessage(null);
    }
  }, [backupQuery.error, backupQuery.isError]);

  useEffect(() => {
    if (cloudStatusQuery.isError) {
      setMessage(readApiError(cloudStatusQuery.error));
      setSuccessMessage(null);
    }
  }, [cloudStatusQuery.error, cloudStatusQuery.isError]);

  useEffect(() => {
    if (cloudBackupsQuery.isError) {
      setMessage(readApiError(cloudBackupsQuery.error));
      setSuccessMessage(null);
    }
  }, [cloudBackupsQuery.error, cloudBackupsQuery.isError]);

  useEffect(() => {
    if (disasterRecoveryQuery.isError) {
      setMessage(readApiError(disasterRecoveryQuery.error));
      setSuccessMessage(null);
    }
  }, [disasterRecoveryQuery.error, disasterRecoveryQuery.isError]);

  const createMutation = useMutation({
    mutationFn: () => client.createDatabaseBackup(),
    onSuccess: (response) => {
      updateBackupQuery(queryClient, response);
      setMessage(null);
      setSuccessMessage(response.message || "数据库备份已创建。");
      void queryClient.invalidateQueries({ queryKey: queryKeys.cloudBackupStatus() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const cleanupMutation = useMutation({
    mutationFn: () =>
      client.cleanupDatabaseBackups({
        body: {
          daysToKeep: cleanupDays,
        },
      }),
    onSuccess: (response) => {
      updateBackupQuery(queryClient, response);
      setMessage(null);
      setSuccessMessage(response.message || "旧备份清理完成。");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const uploadCloudMutation = useMutation({
    mutationFn: () => client.uploadLatestDatabaseBackupToCloud(),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "最新备份已上传到 WebDAV。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.cloudBackupStatus() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.cloudBackupBackups() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const downloadCloudMutation = useMutation({
    mutationFn: () =>
      client.downloadCloudDatabaseBackup({
        body: {
          remoteFileName: cloudDownloadFileName,
        },
      }),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "WebDAV 云备份已下载到本地备份目录。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.backups() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.cloudBackupStatus() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const restoreMutation = useMutation({
    mutationFn: () =>
      client.restoreDatabaseBackup({
        body: {
          backupFileName: restoreFileName,
          confirmationText: restoreConfirmation.trim(),
        },
      }),
    onSuccess: (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "数据库已还原。");
      setRestoreConfirmation("");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const createRecoveryMutation = useMutation({
    mutationFn: () =>
      client.createDisasterRecoveryPackage({
        body: { password: recoveryPassword },
      }),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "持卡机灾难恢复包已创建。");
      setLastRecoveryPackagePath(response.filePath);
      setRecoveryPassword("");
      setRecoveryPasswordConfirmation("");
      await queryClient.invalidateQueries({ queryKey: queryKeys.disasterRecoveryStatus() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const restoreRecoveryMutation = useMutation({
    mutationFn: () =>
      client.restoreDisasterRecoveryPackage({
        body: {
          packagePath: recoveryPackagePath,
          password: recoveryRestorePassword,
          confirmationText: recoveryRestoreConfirmation.trim(),
        },
      }),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "持卡机灾难恢复已排队，请立即重启程序。");
      setRecoveryRestorePassword("");
      setRecoveryRestoreConfirmation("");
      await queryClient.invalidateQueries({ queryKey: queryKeys.disasterRecoveryStatus() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const backups = backupQuery.data?.backups ?? [];
  const cloudStatus = cloudStatusQuery.data ?? null;
  const cloudBackups = cloudBackupsQuery.data?.backups ?? [];
  const disasterRecoveryStatus = disasterRecoveryQuery.data ?? null;
  const cloudBackupsEnabled = canManageSettings && Boolean(cloudStatus?.enabled) && Boolean(cloudStatus?.isConfigured);
  const isBusy =
    backupQuery.isFetching ||
    cloudStatusQuery.isFetching ||
    cloudBackupsQuery.isFetching ||
    disasterRecoveryQuery.isFetching ||
    createMutation.isPending ||
    cleanupMutation.isPending ||
    uploadCloudMutation.isPending ||
    downloadCloudMutation.isPending ||
    restoreMutation.isPending ||
    createRecoveryMutation.isPending ||
    restoreRecoveryMutation.isPending ||
    dataRootMigrationPending;
  const canRestore = canManageSettings && Boolean(restoreFileName) && restoreConfirmation.trim() === "RESTORE" && !isBusy;
  const canUploadCloud =
    canManageSettings &&
    Boolean(cloudStatus?.enabled) &&
    Boolean(cloudStatus?.isConfigured) &&
    backups.length > 0 &&
    !isBusy;
  const canDownloadCloud = cloudBackupsEnabled && Boolean(cloudDownloadFileName) && cloudBackups.length > 0 && !isBusy;
  const canCreateRecovery =
    canManageSettings &&
    Boolean(disasterRecoveryStatus?.supported) &&
    !disasterRecoveryStatus?.pendingRestore &&
    isStrongRecoveryPassword(recoveryPassword) &&
    recoveryPassword === recoveryPasswordConfirmation &&
    !isBusy;
  const canRestoreRecovery =
    canManageSettings &&
    Boolean(disasterRecoveryStatus?.supported) &&
    !disasterRecoveryStatus?.pendingRestore &&
    Boolean(recoveryPackagePath) &&
    isStrongRecoveryPassword(recoveryRestorePassword) &&
    recoveryRestoreConfirmation.trim() === "RECOVER" &&
    !isBusy;

  function refreshBackups() {
    setMessage(null);
    setSuccessMessage(null);
    void backupQuery.refetch();
    void cloudStatusQuery.refetch();
    if (desktopBridgeAvailable) {
      void disasterRecoveryQuery.refetch();
    }
    if (cloudBackupsEnabled) {
      void cloudBackupsQuery.refetch();
    }
  }

  async function chooseNewDataRoot() {
    if (!runtimeStorage?.migrationSupported || dataRootMigrationPending) {
      return;
    }
    if (!await requestConfirmation({
      title: "更换运行数据目录",
      description: "程序将在退出后迁移现有数据，并于下次启动使用新目录。",
      details: [
        "请选择一个空目录，并确保当前账号拥有完整读写权限。",
        "迁移期间请勿关闭计算机或拔出目标磁盘。",
      ],
      confirmLabel: "继续选择目录",
      tone: "warning",
    })) {
      return;
    }

    setDataRootMigrationPending(true);
    setMessage(null);
    setSuccessMessage(null);
    try {
      const result = await scheduleDataRootMigration();
      if (!result) {
        return;
      }
      setSuccessMessage(`${result.message} 新目录：${result.targetDataRoot}`);
      const context = await getRuntimeStorageContext();
      if (context) {
        setRuntimeStorage(context);
      }
    } catch (error) {
      setMessage(readDesktopError(error));
    } finally {
      setDataRootMigrationPending(false);
    }
  }

  async function chooseRecoveryPackage() {
    setMessage(null);
    setSuccessMessage(null);
    try {
      const selected = await selectDisasterRecoveryPackageFile();
      if (selected) {
        setRecoveryPackagePath(selected);
        setRecoveryRestoreConfirmation("");
      }
    } catch (error) {
      setMessage(readDesktopError(error));
    }
  }

  return (
    <section className="form-section backup-management-section" aria-label="数据备份与还原">
      <div className="section-header">
        <h2>数据备份与还原</h2>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新备份" aria-label="刷新备份" disabled={!canManageSettings || isBusy} onClick={refreshBackups}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button
            className="command-button"
            type="button"
            disabled={!canManageSettings || isBusy}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              createMutation.mutate();
            }}
          >
            <Archive size={17} aria-hidden="true" />
            <span>创建备份</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={!canUploadCloud}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              uploadCloudMutation.mutate();
            }}
          >
            <CloudUpload size={17} aria-hidden="true" />
            <span>上传最新备份</span>
          </button>
          {runtimeStorage ? (
            <button
              className="command-button secondary"
              type="button"
              disabled={!canManageSettings || !runtimeStorage.migrationSupported || isBusy}
              title={runtimeStorage.portable ? "便携版请通过复制完整程序目录迁移" : "选择新的空目录，重启后安全迁移"}
              onClick={() => void chooseNewDataRoot()}
            >
              <HardDrive size={17} aria-hidden="true" />
              <span>更换数据目录</span>
            </button>
          ) : null}
          <button
            className="command-button secondary"
            type="button"
            disabled={!canDownloadCloud}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              downloadCloudMutation.mutate();
            }}
          >
            <CloudDownload size={17} aria-hidden="true" />
            <span>下载云备份</span>
          </button>
        </div>
      </div>
      {message ? <InlineNotice tone="error" title="备份操作失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <div className="detail-grid runtime-detail-grid">
        {runtimeStorage ? (
          <>
            <div className="detail-item detail-item-wide">
              <span>当前业务数据目录</span>
              <div className="detail-value-row">
                <strong title={runtimeStorage.dataRoot}>{runtimeStorage.dataRoot}</strong>
                <div className="detail-item-actions">{renderOpenPathAction(runtimeStorage.dataRoot, "打开业务数据目录", onPathError)}</div>
              </div>
            </div>
            <div className="detail-item detail-item-wide">
              <span>存储模式</span>
              <strong title={runtimeStorage.storagePolicy}>{runtimeStorage.portable ? "便携版 · 程序旁存储" : "安装版 · 独立目录"}</strong>
            </div>
          </>
        ) : null}
        <div className="detail-item detail-item-wide">
          <span>备份目录</span>
          <div className="detail-value-row">
            <strong title={backupQuery.data?.backupRoot || "-"}>{backupQuery.data?.backupRoot || "-"}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(backupQuery.data?.backupRoot, "打开备份目录", onPathError)}</div>
          </div>
        </div>
        <CloudBackupStatusDetail status={cloudStatus} />
      </div>
      <div className="backup-action-grid">
        <NumberField label="保留天数" value={cleanupDays} disabled={!canManageSettings || isBusy} step="1" onChange={setCleanupDays} />
        <button
          className="command-button secondary"
          type="button"
          disabled={!canManageSettings || isBusy}
          onClick={() => {
            setMessage(null);
            setSuccessMessage(null);
            cleanupMutation.mutate();
          }}
        >
          <Trash2 size={17} aria-hidden="true" />
          <span>清理旧备份</span>
        </button>
        <SelectField
          label="云端备份"
          value={cloudDownloadFileName}
          disabled={!cloudBackupsEnabled || isBusy || cloudBackups.length === 0}
          options={cloudBackups.map((backup) => ({
            value: backup.fileName,
            label: `${backup.fileName} (${formatBytes(backup.sizeBytes)})`,
          }))}
          onChange={setCloudDownloadFileName}
        />
        <SelectField
          label="还原备份"
          value={restoreFileName}
          disabled={!canManageSettings || isBusy || backups.length === 0}
          options={backups.map((backup) => ({ value: backup.fileName, label: backup.fileName }))}
          onChange={(value) => {
            setRestoreFileName(value);
            setRestoreConfirmation("");
          }}
        />
        <label>
          <span>确认文本</span>
          <input
            value={restoreConfirmation}
            disabled={!canManageSettings || isBusy || backups.length === 0}
            placeholder="RESTORE"
            onChange={(event) => setRestoreConfirmation(event.target.value)}
          />
        </label>
        <button
          className="command-button danger-command"
          type="button"
          disabled={!canRestore}
          onClick={() => {
            setMessage(null);
            setSuccessMessage(null);
            restoreMutation.mutate();
          }}
        >
          <RotateCcw size={17} aria-hidden="true" />
          <span>还原数据库</span>
        </button>
      </div>
      {desktopBridgeAvailable ? (
        <section className="backup-recovery-card" aria-label="持卡机灾难恢复">
          <div className="section-header">
            <div>
              <h3>持卡机灾难恢复</h3>
              <p className="section-description">独立加密包用于整机损坏或更换持卡机，不等同于普通数据库 ZIP 备份。</p>
            </div>
            <ShieldCheck size={22} aria-hidden="true" />
          </div>
          {disasterRecoveryStatus ? (
            <InlineNotice
              tone={disasterRecoveryStatus.pendingRestore ? "warning" : disasterRecoveryStatus.supported ? "info" : "warning"}
              title={disasterRecoveryStatus.pendingRestore ? "恢复任务等待重启" : "恢复包边界"}
            >
              {disasterRecoveryStatus.message} 恢复包不携带许可证或机器绑定，恢复后必须按当前机器码重新激活。
            </InlineNotice>
          ) : null}
          <div className="detail-grid runtime-detail-grid">
            <div className="detail-item detail-item-wide">
              <span>恢复包目录</span>
              <div className="detail-value-row">
                <strong title={disasterRecoveryStatus?.recoveryRoot || "-"}>{disasterRecoveryStatus?.recoveryRoot || "-"}</strong>
                <div className="detail-item-actions">
                  {renderOpenPathAction(disasterRecoveryStatus?.recoveryRoot, "打开恢复包目录", onPathError)}
                </div>
              </div>
            </div>
            {lastRecoveryPackagePath ? (
              <div className="detail-item detail-item-wide">
                <span>本次生成</span>
                <div className="detail-value-row">
                  <strong title={lastRecoveryPackagePath}>{lastRecoveryPackagePath}</strong>
                  <div className="detail-item-actions">{renderOpenPathAction(lastRecoveryPackagePath, "打开恢复包", onPathError)}</div>
                </div>
              </div>
            ) : null}
          </div>
          <div className="backup-action-grid">
            <label>
              <span>新恢复包密码</span>
              <input
                type="password"
                autoComplete="new-password"
                value={recoveryPassword}
                disabled={!canManageSettings || isBusy || !disasterRecoveryStatus?.supported}
                placeholder="至少 12 位，含大小写、数字和符号"
                onChange={(event) => setRecoveryPassword(event.target.value)}
              />
            </label>
            <label>
              <span>再次输入密码</span>
              <input
                type="password"
                autoComplete="new-password"
                value={recoveryPasswordConfirmation}
                disabled={!canManageSettings || isBusy || !disasterRecoveryStatus?.supported}
                onChange={(event) => setRecoveryPasswordConfirmation(event.target.value)}
              />
            </label>
            <button
              className="command-button"
              type="button"
              disabled={!canCreateRecovery}
              onClick={() => {
                setMessage(null);
                setSuccessMessage(null);
                createRecoveryMutation.mutate();
              }}
            >
              <ShieldCheck size={17} aria-hidden="true" />
              <span>创建加密恢复包</span>
            </button>
          </div>
          <div className="backup-action-grid">
            <label>
              <span>待恢复文件</span>
              <input value={recoveryPackagePath} readOnly placeholder="请选择 .edmrecovery 文件" />
            </label>
            <button
              className="command-button secondary"
              type="button"
              disabled={!canManageSettings || isBusy || !disasterRecoveryStatus?.supported}
              onClick={() => void chooseRecoveryPackage()}
            >
              选择恢复包
            </button>
            <label>
              <span>恢复包密码</span>
              <input
                type="password"
                autoComplete="current-password"
                value={recoveryRestorePassword}
                disabled={!canManageSettings || isBusy || !recoveryPackagePath}
                onChange={(event) => setRecoveryRestorePassword(event.target.value)}
              />
            </label>
            <label>
              <span>确认文本</span>
              <input
                value={recoveryRestoreConfirmation}
                disabled={!canManageSettings || isBusy || !recoveryPackagePath}
                placeholder="RECOVER"
                onChange={(event) => setRecoveryRestoreConfirmation(event.target.value)}
              />
            </label>
            <button
              className="command-button danger-command"
              type="button"
              disabled={!canRestoreRecovery}
              onClick={() => {
                setMessage(null);
                setSuccessMessage(null);
                restoreRecoveryMutation.mutate();
              }}
            >
              <RotateCcw size={17} aria-hidden="true" />
              <span>安排灾难恢复</span>
            </button>
          </div>
        </section>
      ) : null}
      <ResponsiveTableFrame className="backup-table-frame" label="数据库备份列表">
        <table className="backup-table" aria-label="数据库备份列表">
          <thead>
            <tr>
              <th>文件</th>
              <th>大小</th>
              <th>创建时间</th>
              <th>更新时间</th>
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
                  <td>{formatRuntimeDate(backup.lastWriteTime)}</td>
                  <td>
                    <div className="table-path-cell">
                      <span title={backup.fullPath}>{backup.fullPath || "-"}</span>
                      {renderOpenPathAction(backup.fullPath, "打开备份文件", onPathError)}
                    </div>
                  </td>
                </tr>
              ))
            ) : (
              <tr>
                <td className="empty-cell" colSpan={5}>
                  {canManageSettings ? (backupQuery.isFetching ? "加载中" : "暂无备份") : "无权限"}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </ResponsiveTableFrame>
    </section>
  );
}

function readDesktopError(error: unknown) {
  if (error instanceof Error) {
    return error.message;
  }
  if (typeof error === "string") {
    return error;
  }
  return "桌面运行目录操作失败。";
}

function isStrongRecoveryPassword(value: string) {
  return value.length >= 12 &&
    value.length <= 128 &&
    /[A-Z]/.test(value) &&
    /[a-z]/.test(value) &&
    /\d/.test(value) &&
    /[^A-Za-z0-9]/.test(value);
}

function CloudBackupStatusDetail({ status }: { status: ApiCloudBackupStatusResponse | null }) {
  const stateText = status
    ? `${status.enabled ? "已启用" : "未启用"} · ${status.isConfigured ? "已配置" : "未配置"}`
    : "加载中";
  const latestText = status?.latestBackupFileName
    ? `${status.latestBackupFileName} (${formatBytes(status.latestBackupSizeBytes)})`
    : "暂无本地备份";

  return (
    <>
      <div className="detail-item">
        <span>WebDAV 云备份</span>
        <strong title={status?.url || stateText}>{stateText}</strong>
      </div>
      <div className="detail-item detail-item-wide">
        <span>最新本地备份</span>
        <strong title={latestText}>{latestText}</strong>
      </div>
    </>
  );
}

function updateBackupQuery(queryClient: ReturnType<typeof useQueryClient>, response: ApiBackupCreateResponse) {
  queryClient.setQueryData<ApiBackupListResponse>(queryKeys.backups(), {
    backups: response.backups,
    backupRoot: response.backupRoot,
    storagePolicy: response.storagePolicy,
  });
}
