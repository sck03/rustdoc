import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Download, RefreshCw, X } from "lucide-react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
  checkTauriUpdate,
  cancelTauriUpdate,
  installTauriUpdate,
  isDesktopBridgeAvailable,
  subscribeToTauriUpdaterProgress,
  type TauriUpdaterCheckResult,
  type TauriUpdaterInstallResult,
  type TauriUpdaterProgress,
} from "../../desktop/desktopBridge.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readUpdaterEndpoint } from "./updaterEndpointModel.ts";

export function UpdateCenterPage({ client }: { client: ExportDocManagerApiClient }) {
  const [checkResult, setCheckResult] = useState<TauriUpdaterCheckResult | null>(null);
  const [installResult, setInstallResult] = useState<TauriUpdaterInstallResult | null>(null);
  const [checkedEndpoint, setCheckedEndpoint] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");
  const [isBusy, setIsBusy] = useState(false);
  const [isCanceling, setIsCanceling] = useState(false);
  const [updateProgress, setUpdateProgress] = useState<TauriUpdaterProgress | null>(null);
  const requestConfirmation = useConfirmation();
  const isDesktop = isDesktopBridgeAvailable();
  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    enabled: isDesktop,
  });
  const updaterEndpoint = readUpdaterEndpoint(settingsQuery.data?.settings);
  const configurationReady = isDesktop && !settingsQuery.isLoading && !settingsQuery.isError;
  const canCheck = configurationReady && !isBusy;
  const canInstall = canCheck &&
    checkedEndpoint === updaterEndpoint &&
    Boolean(checkResult?.installSupported) &&
    Boolean(checkResult?.updateAvailable);
  const canCancel = isBusy && !isCanceling &&
    ["preparing", "downloading", "verifying"].includes(updateProgress?.phase ?? "");

  useEffect(() => {
    if (checkedEndpoint === null || checkedEndpoint === updaterEndpoint) {
      return;
    }

    setCheckedEndpoint(null);
    setCheckResult(null);
    setInstallResult(null);
    setMessage("更新来源已变更，请重新检查更新。");
    setMessageType("success");
  }, [checkedEndpoint, updaterEndpoint]);

  useEffect(() => {
    if (!isDesktop) return undefined;
    let disposed = false;
    let unlisten: (() => void) | undefined;
    void subscribeToTauriUpdaterProgress((progress) => {
      if (!disposed) setUpdateProgress(progress);
    }).then((cleanup) => {
      if (disposed) cleanup();
      else unlisten = cleanup;
    });
    return () => {
      disposed = true;
      unlisten?.();
    };
  }, [isDesktop]);

  async function checkUpdate() {
    setIsBusy(true);
    setMessage(null);
    setInstallResult(null);
    setUpdateProgress(null);
    try {
      const result = await checkTauriUpdate(updaterEndpoint || undefined);
      if (!result) {
        throw new Error("当前不是桌面运行环境，无法检查软件更新。");
      }

      setCheckedEndpoint(updaterEndpoint);
      setCheckResult(result);
      setMessage(result.statusText || (result.updateAvailable ? "发现新版本。" : "当前已是最新版本。"));
      setMessageType(result.errorMessage ? "error" : "success");
    } catch (error) {
      setCheckedEndpoint(null);
      setCheckResult(null);
      setMessage(readApiError(error));
      setMessageType("error");
    } finally {
      setIsBusy(false);
    }
  }

  async function installUpdate() {
    if (checkedEndpoint === null) {
      setMessage("更新来源已变更，请重新检查新版本。");
      setMessageType("error");
      return;
    }

    if (!await requestConfirmation({
      title: "下载并安装更新",
      description: `将安装 v${checkResult?.latestVersion || "新版本"}，完成后程序会自动重启。`,
      details: ["开始前请保存正在编辑的内容。", "下载和签名校验阶段可以取消；进入安装阶段后不能中断。"],
      confirmLabel: "开始更新",
      tone: "warning",
    })) return;

    setIsBusy(true);
    setMessage(null);
    setUpdateProgress({ phase: "preparing", downloadedBytes: 0, statusText: "正在准备更新下载。" });
    try {
      const result = await installTauriUpdate(checkedEndpoint || undefined);
      if (!result) {
        throw new Error("当前不是桌面运行环境，无法安装软件更新。");
      }

      setInstallResult(result);
      setMessage(result.statusText || "更新已安装，正在重启。");
      setMessageType(result.success ? "success" : "error");
    } catch (error) {
      const errorMessage = readApiError(error);
      const canceled = errorMessage.includes("已取消");
      setMessage(canceled ? "更新下载已取消，未修改当前安装。" : errorMessage);
      setMessageType(canceled ? "success" : "error");
    } finally {
      setIsBusy(false);
      setIsCanceling(false);
    }
  }

  async function cancelUpdate() {
    setIsCanceling(true);
    try {
      const result = await cancelTauriUpdate();
      if (!result) {
        throw new Error("当前不是桌面运行环境，无法取消软件更新。");
      }

      setMessage(result.statusText);
      setMessageType(result.accepted ? "success" : "error");
    } catch (error) {
      setMessage(readApiError(error));
      setMessageType("error");
    } finally {
      setIsCanceling(false);
    }
  }

  return (
    <section className="work-surface" aria-label="软件更新">
      <div className="toolbar update-center-toolbar">
        <div className="toolbar-summary">
          <strong>软件更新</strong>
          <span>{isDesktop ? "检查并安装新版本" : "仅桌面端可用"}</span>
        </div>
        <div className="toolbar-actions">
          <button className="command-button secondary" type="button" disabled={!canCheck} onClick={checkUpdate}>
            <RefreshCw size={17} aria-hidden="true" />
            <span>检查更新</span>
          </button>
          <button className="command-button" type="button" disabled={!canInstall} onClick={installUpdate}>
            <Download size={17} aria-hidden="true" />
            <span>下载并安装</span>
          </button>
          {isBusy ? (
            <button className="command-button secondary" type="button" disabled={!canCancel} onClick={cancelUpdate}>
              <X size={17} aria-hidden="true" />
              <span>{isCanceling ? "正在取消" : "取消下载"}</span>
            </button>
          ) : null}
        </div>
      </div>

      {settingsQuery.isError ? (
        <InlineNotice tone="error" title="暂时无法读取更新设置">
          {readApiError(settingsQuery.error)}
        </InlineNotice>
      ) : null}
      {message ? <InlineNotice tone={messageType === "error" ? "error" : "success"}>{message}</InlineNotice> : null}

      <section className="form-section" aria-label="更新状态">
        <div className="detail-grid update-center-detail-grid">
          <DetailItem label="当前版本" value={formatVersion(checkResult?.currentVersion)} />
          <DetailItem label="最新版本" value={formatVersion(checkResult?.latestVersion)} />
          <DetailItem
            label="检查结果"
            value={checkResult
              ? (checkResult.updateAvailable
                ? (checkResult.installSupported ? "发现可安装的新版本" : "发现新版便携包")
                : "已是最新版本")
              : "-"}
          />
          <DetailItem label="发布时间" value={formatDateTime(checkResult?.date)} />
          <DetailItem label="安装版本" value={formatVersion(installResult?.installedVersion)} />
        </div>
        {updateProgress ? (
          <div className="update-progress" aria-live="polite">
            <div className="detail-value-row">
              <strong>{updateProgress.statusText}</strong>
              <span>{formatUpdateProgress(updateProgress)}</span>
            </div>
            <progress max={100} value={updateProgress.progressPercent ?? undefined} />
          </div>
        ) : null}
      </section>

      <section className="form-section update-release-section" aria-label="更新日志">
        <div className="section-header">
          <div>
            <h2>更新日志</h2>
            <span>{checkResult?.updateAvailable ? `v${checkResult.latestVersion}` : "未发现更新"}</span>
          </div>
        </div>
        <div className="update-release-notes">{checkResult?.body?.trim() || "暂无更新日志。"}</div>
      </section>
    </section>
  );
}

function DetailItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="detail-item">
      <span>{label}</span>
      <strong title={value}>{value}</strong>
    </div>
  );
}

function formatVersion(value?: string) {
  return value?.trim() ? `v${value.trim()}` : "-";
}

function formatDateTime(value?: string) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function formatUpdateProgress(progress: TauriUpdaterProgress) {
  if (typeof progress.progressPercent === "number") return `${progress.progressPercent}%`;
  if (progress.phase === "verifying") return "校验中";
  if (progress.phase === "installing") return "安装中";
  if (progress.phase === "restarting") return "即将重启";
  if (progress.phase === "canceled") return "已取消";
  return "准备中";
}
