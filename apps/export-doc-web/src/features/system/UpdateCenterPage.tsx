import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Download, RefreshCw, Settings } from "lucide-react";
import { useNavigate } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
  checkTauriUpdate,
  installTauriUpdate,
  isDesktopBridgeAvailable,
  type TauriUpdaterCheckResult,
  type TauriUpdaterInstallResult,
} from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import {
  describeUpdaterEndpoint,
  isInsecureHttpUpdaterEndpoint,
  readUpdaterEndpoint,
} from "./updaterEndpointModel.ts";

export function UpdateCenterPage({ client }: { client: ExportDocManagerApiClient }) {
  const navigate = useNavigate();
  const [checkResult, setCheckResult] = useState<TauriUpdaterCheckResult | null>(null);
  const [installResult, setInstallResult] = useState<TauriUpdaterInstallResult | null>(null);
  const [checkedEndpoint, setCheckedEndpoint] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");
  const [isBusy, setIsBusy] = useState(false);
  const isDesktop = isDesktopBridgeAvailable();
  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    enabled: isDesktop,
  });
  const updaterEndpoint = readUpdaterEndpoint(settingsQuery.data?.settings);
  const usesInsecureHttp = isInsecureHttpUpdaterEndpoint(updaterEndpoint);
  const configurationReady = isDesktop && !settingsQuery.isLoading && !settingsQuery.isError;
  const canCheck = configurationReady && !isBusy;
  const canInstall = canCheck &&
    checkedEndpoint === updaterEndpoint &&
    Boolean(checkResult?.updateAvailable);

  useEffect(() => {
    if (checkedEndpoint === null || checkedEndpoint === updaterEndpoint) {
      return;
    }

    setCheckedEndpoint(null);
    setCheckResult(null);
    setInstallResult(null);
    setMessage("更新地址已变化，请重新检查更新。");
    setMessageType("success");
  }, [checkedEndpoint, updaterEndpoint]);

  async function checkUpdate() {
    setIsBusy(true);
    setMessage(null);
    setInstallResult(null);
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
      setMessage("请先使用当前更新地址检查新版本。");
      setMessageType("error");
      return;
    }

    setIsBusy(true);
    setMessage(null);
    try {
      const result = await installTauriUpdate(checkedEndpoint || undefined);
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
          <button className="command-button secondary" type="button" disabled={!canCheck} onClick={checkUpdate}>
            <RefreshCw size={17} aria-hidden="true" />
            <span>检查更新</span>
          </button>
          <button className="command-button" type="button" disabled={!canInstall} onClick={installUpdate}>
            <Download size={17} aria-hidden="true" />
            <span>下载并安装</span>
          </button>
        </div>
      </div>

      {settingsQuery.isError ? (
        <InlineNotice tone="error" title="更新配置读取失败">
          {readApiError(settingsQuery.error)}
        </InlineNotice>
      ) : null}
      {message ? <InlineNotice tone={messageType === "error" ? "error" : "success"}>{message}</InlineNotice> : null}

      <section className="form-section update-config-section" aria-label="更新配置">
        <div className="section-header">
          <div>
            <h2>更新配置</h2>
            <span>{isDesktop ? "管理员统一控制更新来源" : "仅桌面端生效"}</span>
          </div>
        </div>
        <InlineNotice
          tone="info"
          title="更新地址由管理员配置，签名公钥固定在安装包内"
          action={(
            <button className="command-button secondary" type="button" onClick={() => navigate("/settings?section=updater")}>
              <Settings size={16} aria-hidden="true" />
              <span>打开系统设置</span>
            </button>
          )}
        >
          更新地址可以在 GitHub、自建服务器和企业内网之间切换；页面和运行配置都不能替换签名公钥，因此更换地址不会改变客户端信任根。
        </InlineNotice>
        {usesInsecureHttp ? (
          <InlineNotice tone="warning" title="当前使用公司内网 HTTP 更新地址">
            HTTP 只适用于受控内网、专用 VLAN 或可信 VPN。它不会绕过安装包签名校验，但传输内容可能被旁路观察或阻断；公网和跨不可信网络仍应使用 HTTPS。
          </InlineNotice>
        ) : null}
        <div className="detail-grid update-center-detail-grid">
          <DetailItem label="当前更新地址" value={describeUpdaterEndpoint(updaterEndpoint)} wide />
          <DetailItem label="地址来源" value={updaterEndpoint ? "管理员系统设置" : "安装包默认配置"} />
          <DetailItem label="传输方式" value={usesInsecureHttp ? "HTTP（可信内网）" : updaterEndpoint ? "HTTPS" : "由默认地址决定"} />
          <DetailItem label="签名信任" value="安装包内置公钥（不可配置）" />
        </div>
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
