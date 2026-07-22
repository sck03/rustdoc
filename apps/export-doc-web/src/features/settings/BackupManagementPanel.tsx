import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, CloudDownload, CloudUpload, RefreshCw, RotateCcw, Trash2 } from "lucide-react";
import type {
  ApiBackupCreateResponse,
  ApiBackupListResponse,
  ApiCloudBackupStatusResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { NumberField, SelectField } from "../../ui/FormFields.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
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
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [cleanupDays, setCleanupDays] = useState(30);
  const [restoreFileName, setRestoreFileName] = useState("");
  const [restoreConfirmation, setRestoreConfirmation] = useState("");
  const [cloudDownloadFileName, setCloudDownloadFileName] = useState("");

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

  const backups = backupQuery.data?.backups ?? [];
  const cloudStatus = cloudStatusQuery.data ?? null;
  const cloudBackups = cloudBackupsQuery.data?.backups ?? [];
  const cloudBackupsEnabled = canManageSettings && Boolean(cloudStatus?.enabled) && Boolean(cloudStatus?.isConfigured);
  const isBusy =
    backupQuery.isFetching ||
    cloudStatusQuery.isFetching ||
    cloudBackupsQuery.isFetching ||
    createMutation.isPending ||
    cleanupMutation.isPending ||
    uploadCloudMutation.isPending ||
    downloadCloudMutation.isPending ||
    restoreMutation.isPending;
  const canRestore = canManageSettings && Boolean(restoreFileName) && restoreConfirmation.trim() === "RESTORE" && !isBusy;
  const canUploadCloud =
    canManageSettings &&
    Boolean(cloudStatus?.enabled) &&
    Boolean(cloudStatus?.isConfigured) &&
    backups.length > 0 &&
    !isBusy;
  const canDownloadCloud = cloudBackupsEnabled && Boolean(cloudDownloadFileName) && cloudBackups.length > 0 && !isBusy;

  function refreshBackups() {
    setMessage(null);
    setSuccessMessage(null);
    void backupQuery.refetch();
    void cloudStatusQuery.refetch();
    if (cloudBackupsEnabled) {
      void cloudBackupsQuery.refetch();
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
      {message ? <div className="alert">{message}</div> : null}
      {successMessage ? <div className="success-alert">{successMessage}</div> : null}
      <div className="detail-grid runtime-detail-grid">
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
