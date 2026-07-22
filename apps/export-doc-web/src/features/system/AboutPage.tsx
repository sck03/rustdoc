import { useQuery } from "@tanstack/react-query";
import { Info, RefreshCw } from "lucide-react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";

export function AboutPage({ client }: { client: ExportDocManagerApiClient }) {
  const healthQuery = useQuery({
    queryKey: queryKeys.health(),
    queryFn: () => client.getHealth(),
  });

  const health = healthQuery.data ?? null;
  const isBusy = healthQuery.isFetching;
  const errorMessage = healthQuery.isError ? readApiError(healthQuery.error) : "";
  const productVersionText = formatVersion(health?.productVersion || health?.informationalVersion);
  const isDesktopRuntime = isDesktopBridgeAvailable();

  function refresh() {
    void healthQuery.refetch();
  }

  return (
    <section className="work-surface about-surface" aria-label="关于">
      <div className="toolbar about-toolbar">
        <div className="toolbar-summary">
          <strong>出口单证管理系统</strong>
          <span>{isDesktopRuntime ? "本地优先桌面工作区" : "局域网与容器协同工作区"}</span>
        </div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新" aria-label="刷新" disabled={isBusy} onClick={refresh}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {errorMessage ? <div className="alert">{errorMessage}</div> : null}

      <section className="form-section" aria-label="产品信息">
        <div className="section-header">
          <div>
            <h2>产品信息</h2>
            <span>{productVersionText}</span>
          </div>
          <Info size={18} aria-hidden="true" />
        </div>
        <div className="detail-grid about-detail-grid">
          <DetailItem label="产品" value="出口单证管理系统" />
          <DetailItem label="版本" value={productVersionText} />
          <DetailItem label="API 版本" value={formatVersion(health?.informationalVersion || health?.productVersion)} />
          <DetailItem label="形态" value={isDesktopRuntime ? "绿色便携桌面版" : "局域网 / 容器 Web 版"} />
          <DetailItem label="运行方式" value={isDesktopRuntime ? "Tauri 桌面端" : "浏览器协同端"} />
          <DetailItem label="编写者" value="steven.sck 施" />
          <DetailItem label="版权" value="Copyright © steven.sck 施 2026" wide />
        </div>
      </section>

      <section className="form-section" aria-label="运行概览">
        <div className="section-header">
          <div>
            <h2>运行概览</h2>
            <span>{health?.status === "ok" ? "API 正常" : "读取中"}</span>
          </div>
        </div>
        <div className="detail-grid about-runtime-summary-grid">
          <DetailItem label="API 状态" value={health?.status || "-"} />
          <DetailItem label="检查时间" value={formatDateTime(health?.checkedAt)} />
          <DetailItem label="数据库模式" value={health?.databaseProvider || "-"} />
          <DetailItem label="浏览器环境" value={readBrowserRuntimeText()} />
        </div>
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

function readBrowserRuntimeText() {
  const userAgent = window.navigator.userAgent.trim();
  return userAgent || window.navigator.platform || "-";
}
