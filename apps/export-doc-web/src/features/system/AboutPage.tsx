import { useQuery } from "@tanstack/react-query";
import { Info } from "lucide-react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
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
  const isDesktopRuntime = isDesktopBridgeAvailable();
  const productVersionText = formatVersion(health?.productVersion || health?.informationalVersion);

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
            <span>当前安装与授权版本</span>
          </div>
          <Info size={18} aria-hidden="true" />
        </div>
        <div className="detail-grid about-detail-grid">
          <DetailItem label="产品" value={product.displayName} />
          <DetailItem label="版本" value={productVersionText} />
          <DetailItem label="版本形态" value={product.editionName} />
          <DetailItem label="使用方式" value={isDesktopRuntime ? "桌面工作区" : "多人协作工作区"} />
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

function formatVersion(value?: string) {
  return value?.trim() ? `v${value.trim()}` : "-";
}
