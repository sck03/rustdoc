import { useState } from "react";
import { Download, RefreshCw } from "lucide-react";
import {
  checkTauriUpdate,
  installTauriUpdate,
  isDesktopBridgeAvailable,
  type TauriUpdaterCheckResult,
  type TauriUpdaterInstallResult,
} from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";

export function UpdateCenterPage() {
  const [checkResult, setCheckResult] = useState<TauriUpdaterCheckResult | null>(null);
  const [installResult, setInstallResult] = useState<TauriUpdaterInstallResult | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");
  const [isBusy, setIsBusy] = useState(false);
  const isDesktop = isDesktopBridgeAvailable();
  const canInstall = isDesktop && Boolean(checkResult?.updateAvailable) && !isBusy;

  async function checkUpdate() {
    setIsBusy(true);
    setMessage(null);
    setInstallResult(null);
    try {
      const result = await checkTauriUpdate();
      if (!result) {
        throw new Error("当前不是桌面运行环境，无法检查软件更新。");
      }

      setCheckResult(result);
      setMessage(result.statusText || (result.updateAvailable ? "发现新版本。" : "当前已是最新版本。"));
      setMessageType(result.errorMessage ? "error" : "success");
    } catch (error) {
      setMessage(readApiError(error));
      setMessageType("error");
    } finally {
      setIsBusy(false);
    }
  }

  async function installUpdate() {
    setIsBusy(true);
    setMessage(null);
    try {
      const result = await installTauriUpdate();
      if (!result) {
        throw new Error("当前不是桌面运行环境，无法安装软件更新。");
      }

      setInstallResult(result);
      setMessage(result.statusText || "更新已安装，正在重启。");
      setMessageType(result.success ? "success" : "error");
    } catch (error) {
      setMessage(readApiError(error));
      setMessageType("error");
    } finally {
      setIsBusy(false);
    }
  }

  return (
    <section className="work-surface update-center-surface" aria-label="软件更新">
      <div className="toolbar update-center-toolbar">
        <div className="toolbar-summary">
          <strong>软件更新</strong>
          <span>{isDesktop ? "检查并安装新版本" : "仅桌面端可用"}</span>
        </div>
        <div className="toolbar-actions">
          <button className="command-button secondary" type="button" disabled={!isDesktop || isBusy} onClick={checkUpdate}>
            <RefreshCw size={17} aria-hidden="true" />
            <span>检查更新</span>
          </button>
          <button className="command-button" type="button" disabled={!canInstall} onClick={installUpdate}>
            <Download size={17} aria-hidden="true" />
            <span>下载并安装</span>
          </button>
        </div>
      </div>

      {message ? <InlineNotice tone={messageType === "error" ? "error" : "success"}>{message}</InlineNotice> : null}

      <section className="form-section update-config-section" aria-label="更新安全说明">
        <div className="section-header">
          <div>
            <h2>可信更新</h2>
            <span>{isDesktop ? "更新配置由正式安装包内置" : "仅桌面端生效"}</span>
          </div>
        </div>
        <InlineNotice tone="info" title="更新地址和签名公钥不可在页面修改">
          正式发布包只接受构建时内置的 HTTPS 更新源，并使用内置公钥验证更新签名。这样可以避免临时修改地址或公钥后安装未经信任的软件包。
        </InlineNotice>
      </section>

      <section className="form-section" aria-label="更新状态">
        <div className="detail-grid update-center-detail-grid">
          <DetailItem label="当前版本" value={formatVersion(checkResult?.currentVersion)} />
          <DetailItem label="最新版本" value={formatVersion(checkResult?.latestVersion)} />
          <DetailItem label="更新可用" value={checkResult?.updateAvailable ? "是" : "否"} />
          <DetailItem label="目标平台" value={checkResult?.target || "-"} />
          <DetailItem label="下载地址" value={checkResult?.downloadUrl || "-"} wide />
          <DetailItem label="发布时间" value={formatDateTime(checkResult?.date)} />
          <DetailItem label="安装版本" value={formatVersion(installResult?.installedVersion)} />
          <DetailItem label="验证方式" value="安装包内置签名公钥" />
          <DetailItem label="重启策略" value={installResult?.restartPolicy || "-"} wide />
        </div>
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

function DetailItem({ label, value, wide }: { label: string; value: string; wide?: boolean }) {
  return (
    <div className={wide ? "detail-item detail-item-wide" : "detail-item"}>
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
