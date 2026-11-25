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
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [lastPackage, setLastPackage] = useState<ApiSupportPackageResponse | null>(null);
  const isDesktop = isDesktopBridgeAvailable();

  const createMutation = useMutation({
    mutationFn: () => runAbortableOperation(async (signal) => {
      if (isDesktop) {
        const response = await client.saveSupportPackageToRuntime({ signal });
        return { mode: "desktop" as const, response };
      }

      const job = await client.downloadSupportPackage({ signal });
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

  return (
    <section className="form-section support-package-section" aria-label="问题诊断包">
      <div className="section-header">
        <div>
          <h2>问题诊断包</h2>
          <p className="section-description">导出脱敏诊断摘要，不包含业务文件或数据库备份。</p>
        </div>
        <div className="toolbar-actions">
          <button
            className="command-button"
            type="button"
            disabled={!supportPackageCanCreate}
            onClick={() => {
              setMessage(null);
              setSuccessMessage(null);
              createMutation.mutate();
            }}
          >
            <LifeBuoy size={17} aria-hidden="true" />
            <span>{createMutation.isPending ? "正在生成支持包" : "导出支持包"}</span>
          </button>
        </div>
      </div>
      {message ? <InlineNotice tone="error" title="支持包导出失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
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
