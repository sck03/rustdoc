import { useQuery } from "@tanstack/react-query";
import { Info, RefreshCw } from "lucide-react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import type { ProductEditionPresentation } from "../../app/productEdition.ts";

export function AboutPage({
  client,
  product,
}: {
  client: ExportDocManagerApiClient;
  product: ProductEditionPresentation;
}) {
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
          <strong>{product.displayName}</strong>
          <span>{product.loginTagline} · {isDesktopRuntime ? "本地优先桌面工作区" : "局域网与容器协同工作区"}</span>
        </div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新" aria-label="刷新" disabled={isBusy} onClick={refresh}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {errorMessage ? <InlineNotice tone="error" title="系统信息加载失败">{errorMessage}</InlineNotice> : null}

      <section className="form-section" aria-label="产品信息">
        <div className="section-header">
          <div>
            <h2>产品信息</h2>
            <span>{productVersionText}</span>
          </div>
          <Info size={18} aria-hidden="true" />
        </div>
        <div className="detail-grid about-detail-grid">
          <DetailItem label="产品" value={product.displayName} />
          <DetailItem label="版本形态" value={product.editionName} />
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

      <section className="form-section" aria-label="字体与第三方许可">
        <div className="section-header">
          <div>
            <h2>字体与第三方许可</h2>
            <span>正式报表采用可随软件分发的开源字体</span>
          </div>
        </div>
        <div className="detail-grid about-font-license-grid">
          <DetailItem label="PDF / 打印无衬线字体" value="Noto Sans CJK SC" />
          <DetailItem label="中文正式单据衬线字体" value="Noto Serif CJK SC" />
          <DetailItem label="字体许可证" value="SIL Open Font License 1.1" />
          <DetailItem label="许可证文件" value="Resources/Fonts/OpenSource/OFL-Noto-CJK.txt" wide />
        </div>
        <p className="about-license-note">
          微软雅黑、Segoe UI、宋体、Arial、SF Pro、PingFang 等仅可作为操作系统已有字体的回退名称；程序安装包不会复制或分发这些专有字体文件。
        </p>
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
