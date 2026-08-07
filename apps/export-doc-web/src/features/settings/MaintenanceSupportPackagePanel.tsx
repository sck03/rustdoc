import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { LifeBuoy } from "lucide-react";
import type { ApiSupportPackageResponse } from "../../api/index.ts";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { formatBytes } from "./settingsFormatters.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useAbortableOperation } from "../../ui/useAbortableOperation.ts";

export function SupportPackagePanel({
  client,
  canManageSettings,
  onPathError,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  onPathError: (message: string) => void;
}) {
  const runAbortableOperation = useAbortableOperation();
  const requestConfirmation = useConfirmation();
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [lastPackage, setLastPackage] = useState<ApiSupportPackageResponse | null>(null);
  const [includeLatestDatabaseBackup, setIncludeLatestDatabaseBackup] = useState(false);
  const [includeSampleFiles, setIncludeSampleFiles] = useState(false);
  const includeOptionalFiles = includeLatestDatabaseBackup || includeSampleFiles;
  const isDesktop = isDesktopBridgeAvailable();

  const createMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      const body = {
        includeLatestDatabaseBackup,
        includeSampleFiles,
        confirmationText: includeOptionalFiles ? "INCLUDE OPTIONAL FILES" : "",
      };
      if (isDesktop) {
        const response = await client.saveSupportPackageToRuntime({ body }, { signal });
        return { mode: "desktop" as const, response };
      }

      const job = await client.downloadSupportPackage({ body }, { signal });
      await downloadJobResultWhenReady(
        client,
        job,
        `support-package-${new Date().toISOString().replace(/[:.]/g, "-")}.zip`,
        { timeoutMs: 60 * 60 * 1000, pollIntervalMs: 2_000, signal },
      );
      return { mode: "browser" as const };
    }),
    onSuccess: (result) => {
      setLastPackage(result.mode === "desktop" ? result.response : null);
      setMessage(null);
      setSuccessMessage(result.mode === "desktop" ? (result.response.message || "支持包已导出。") : "支持包已交给浏览器下载。");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });
  const supportPackageCanCreate = canManageSettings && !createMutation.isPending;

  async function handleCreateSupportPackage() {
    if (includeOptionalFiles && !await requestConfirmation({ title: "生成包含可选文件的支持包", description: "支持包将包含所选的数据库备份或样张文件。", details: ["请确认其中不含不应交给技术支持的敏感业务资料。"], confirmLabel: "确认生成" })) {
      return;
    }

    createMutation.mutate();
  }

  return (
    <section className="form-section support-package-section" aria-label="问题诊断包">
      <div className="section-header">
        <div>
          <h2>问题诊断包</h2>
          <p className="section-description">遇到无法启动、报表异常等问题时，可导出资料交给技术支持。</p>
        </div>
        <div className="toolbar-actions">
          <button
            className="command-button"
            type="button"
            disabled={!supportPackageCanCreate}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              void handleCreateSupportPackage();
            }}
          >
            <LifeBuoy size={17} aria-hidden="true" />
            <span>{createMutation.isPending ? "正在生成支持包" : "导出支持包"}</span>
          </button>
        </div>
      </div>
      {message ? <InlineNotice tone="error" title="支持包导出失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <div className="backup-action-grid">
        <label className="settings-check">
          <input
            type="checkbox"
            checked={includeLatestDatabaseBackup}
            disabled={!canManageSettings || createMutation.isPending}
            onChange={(event) => setIncludeLatestDatabaseBackup(event.target.checked)}
          />
          <span>包含最新数据库备份</span>
        </label>
        <label className="settings-check">
          <input
            type="checkbox"
            checked={includeSampleFiles}
            disabled={!canManageSettings || createMutation.isPending}
            onChange={(event) => setIncludeSampleFiles(event.target.checked)}
          />
          <span>包含样张文件</span>
        </label>
      </div>
      {isDesktop ? <div className="detail-grid runtime-detail-grid">
        <div className="detail-item detail-item-wide">
          <span>支持包目录</span>
          <div className="detail-value-row">
            <strong title={lastPackage?.supportPackageRoot || "-"}>{lastPackage?.supportPackageRoot || "-"}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(lastPackage?.supportPackageRoot, "打开支持包目录", onPathError)}</div>
          </div>
        </div>
        <div className="detail-item detail-item-wide">
          <span>最近支持包</span>
          <div className="detail-value-row">
            <strong title={lastPackage?.fullPath || "-"}>{lastPackage ? `${lastPackage.fileName} (${formatBytes(lastPackage.sizeBytes)})` : "-"}</strong>
            <div className="detail-item-actions">{renderOpenPathAction(lastPackage?.fullPath, "打开支持包", onPathError)}</div>
          </div>
        </div>
      </div> : null}
    </section>
  );
}
