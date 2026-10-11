import { useQuery } from "@tanstack/react-query";
import { Info } from "lucide-react";
import type { ApiHealthResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import type { ProductEditionPresentation } from "../../app/productEdition.ts";
import { Button } from "../../ui/Button.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";

export function AboutPage({
  client,
  product,
}: {
  client: ExportDocManagerApiClient;
  product: ProductEditionPresentation;
}) {
  const healthQuery = useQuery({
    queryKey: queryKeys.health(),
    queryFn: ({ signal }) => client.getHealth({ signal }),
    select: selectProductInfo,
  });
  const health = healthQuery.data ?? null;
  const isDesktopRuntime = isDesktopBridgeAvailable();
  const unavailable = healthQuery.isPending ? "读取中…" : "未获取";

  return (
    <section className="work-surface about-surface" aria-label="关于">
      <div className="toolbar about-toolbar">
        <div className="toolbar-summary">
          <strong>{product.displayName}</strong>
          <span>{product.loginTagline}</span>
        </div>
      </div>

      <section className="form-section" aria-label="产品信息">
        <div className="section-header">
          <div>
            <h2>产品信息</h2>
            <span>当前程序版本与运行方式</span>
          </div>
          <Info size={18} aria-hidden="true" />
        </div>
        {healthQuery.isError ? <InlineNotice tone="error" title="版本信息读取失败"
          action={<Button variant="secondary" disabled={healthQuery.isFetching} onClick={() => void healthQuery.refetch()}>
            {healthQuery.isFetching ? "正在重试…" : "重试"}
          </Button>}>
          {health ? "以下版本信息为上次读取结果，请重试以核实当前程序。" : "暂时无法取得程序版本，请重试。"}
        </InlineNotice> : null}
        <div className="detail-grid about-detail-grid">
          <DetailItem label="产品" value={product.displayName} />
          <DetailItem label="版本" value={health?.version || unavailable} />
          <DetailItem label="版本形态" value={product.editionName} />
          <DetailItem label="使用方式" value={isDesktopRuntime ? "单机工作区" : "多人协作工作区"} />
          <DetailItem label="构建标识" value={health?.build || unavailable} wide />
          <DetailItem label="发行方" value="steven.sck 施" />
          <DetailItem label="版权" value="Copyright © 2026 steven.sck 施" wide />
        </div>
      </section>

      <section className="form-section" aria-label="许可与支持">
        <div className="section-header">
          <div>
            <h2>许可与支持</h2>
            <span>开源组件许可随安装包提供</span>
          </div>
        </div>
        <div className="detail-grid about-font-license-grid">
          <DetailItem label="报表字体" value="Noto Sans CJK SC / Noto Serif CJK SC" wide />
          <DetailItem label="字体许可" value="SIL Open Font License 1.1" />
          <DetailItem label="技术支持" value="请联系系统管理员或软件服务商" />
          <DetailItem
            label="数据说明"
            value={isDesktopRuntime ? "业务数据由本机运行目录统一管理" : "业务数据由企业服务器统一管理"}
            wide
          />
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

function selectProductInfo(health: ApiHealthResponse) {
  const version = health.productVersion?.trim() || health.informationalVersion?.trim();
  const build = health.informationalVersion?.trim();
  return {
    version: version ? `v${version.replace(/^v/iu, "")}` : "未提供",
    build: build && build !== version ? build : "未提供",
  };
}
