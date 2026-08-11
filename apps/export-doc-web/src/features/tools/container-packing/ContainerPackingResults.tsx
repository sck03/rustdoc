import { lazy, Suspense, type MutableRefObject } from "react";
import type { ApiContainerPackingAnalysisDto } from "../../../api/index.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";
import { PageState } from "../../../ui/PageState.tsx";
import {
  containerPackingZoneOptions,
  formatPackingPercent,
  type ContainerPackingCargoRow,
  type ContainerPackingRenderModeValue,
} from "./containerPackingModel.ts";
import {
  ContainerPackingVisualization,
} from "./ContainerPackingVisualization.tsx";
import type { ContainerPackingVisualizationDimensions } from "./containerPackingVisualizationModel.ts";

const ContainerPackingScene3d = lazy(() => import("./ContainerPackingScene3d.tsx"));

type Props = {
  analysis: ApiContainerPackingAnalysisDto | null;
  canAnalyze: boolean;
  cargoRows: ContainerPackingCargoRow[];
  isAnalyzing: boolean;
  pdfRootRef: MutableRefObject<HTMLDivElement | null>;
  renderMode: ContainerPackingRenderModeValue;
  visualizationDimensions: ContainerPackingVisualizationDimensions | null;
  onAnalyze(): void;
};

export function ContainerPackingResults({
  analysis,
  canAnalyze,
  cargoRows,
  isAnalyzing,
  pdfRootRef,
  renderMode,
  visualizationDimensions,
  onAnalyze,
}: Props) {
  if (!analysis) {
    return (
      <section className="container-packing-preview-placeholder" aria-label="装柜效果图状态">
        <PageState
          title="3D 与伪 3D 效果图等待分析"
          description="货物或柜型修改后，点击“立即刷新”或“分析”生成装载方案；生成后会同时显示可旋转 3D、俯视、侧视和柜门图。"
          action={(
            <button className="command-button" type="button" disabled={!canAnalyze || isAnalyzing} onClick={onAnalyze}>
              {isAnalyzing ? "正在分析…" : "立即生成效果图"}
            </button>
          )}
        />
      </section>
    );
  }

  return (
    <div
      className="container-packing-result"
      ref={(element) => { pdfRootRef.current = element; }}
      data-container-packing-pdf
    >
      <PackingCargoSnapshot cargoRows={cargoRows} />
      <PackingSummary analysis={analysis} />
      <PackingInstructions analysis={analysis} />
      {visualizationDimensions ? (
        <>
          <Suspense fallback={<SceneLoadingState />}>
            <ContainerPackingScene3d
              analysis={analysis}
              dimensions={visualizationDimensions}
              renderMode={renderMode}
            />
          </Suspense>
          <ContainerPackingVisualization
            analysis={analysis}
            dimensions={visualizationDimensions}
            renderMode={renderMode}
          />
        </>
      ) : null}
    </div>
  );
}

function PackingCargoSnapshot({ cargoRows }: { cargoRows: ContainerPackingCargoRow[] }) {
  return (
    <div className="container-packing-pdf-cargo" aria-hidden="true">
      <h2>货物清单</h2>
      <table>
        <thead>
          <tr><th>货物</th><th>尺寸（cm）</th><th>单件重量</th><th>数量</th><th>装载区域</th><th>托盘</th></tr>
        </thead>
        <tbody>
          {cargoRows.map((row) => (
            <tr key={`pdf-${row.id}`}>
              <td>{row.name || "未命名货物"}</td>
              <td>{row.length} × {row.width} × {row.height}</td>
              <td>{row.weight || "-"}</td>
              <td>{row.quantity || "-"}</td>
              <td>{containerPackingZoneOptions.find((option) => option.value === row.preferredZone)?.label || "自动"}</td>
              <td>{row.usePallet ? `是（每托 ${row.unitsPerPallet || "-"}）` : "否"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PackingSummary({ analysis }: { analysis: ApiContainerPackingAnalysisDto }) {
  return (
    <div className="detail-grid packing-summary-grid">
      <SummaryItem label="装载件数" value={`${formatPlainNumber(analysis.packedPackages)} / ${formatPlainNumber(analysis.totalPackages)}`} />
      <SummaryItem label="未装件数" value={formatPlainNumber(analysis.unpackedPackages)} />
      <SummaryItem label="估算柜数" value={formatPlainNumber(analysis.estimatedContainerCount)} />
      <SummaryItem label="体积利用" value={formatPackingPercent(analysis.volumeUtilizationPercent)} />
      <SummaryItem label="装载体积" value={`${formatPlainNumber(analysis.packedVolume)} / ${formatPlainNumber(analysis.totalVolume)}`} />
      <SummaryItem label="重量利用" value={formatPackingPercent(analysis.weightUtilizationPercent)} />
      <SummaryItem label="装载重量" value={`${formatPlainNumber(analysis.packedWeight)} / ${formatPlainNumber(analysis.totalWeight)}`} />
      <SummaryItem label="重心状态" value={analysis.isCenterOfGravityWithinTolerance ? "正常" : "超限"} />
    </div>
  );
}

function SummaryItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="detail-item">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function PackingInstructions({ analysis }: { analysis: ApiContainerPackingAnalysisDto }) {
  return (
    <div className="container-packing-pdf-instructions" aria-hidden="true">
      <h2>现场装柜提示</h2>
      <ol>
        <li>先确认实际柜型和内尺寸与作业单一致，再从柜头向柜门方向装载。</li>
        <li>按效果图颜色核对货物，结合俯视、侧视和柜门图确认每批货物位置。</li>
        <li>{analysis.unpackedPackages > 0 ? `当前仍有 ${formatPlainNumber(analysis.unpackedPackages)} 件未装入，请另行安排。` : "本方案货物已全部装入。"}</li>
        <li>{analysis.isCenterOfGravityWithinTolerance ? "装载重心在设定范围内。" : "装载重心超出设定范围，现场装柜前请复核配重和固定方式。"}</li>
      </ol>
    </div>
  );
}

function SceneLoadingState() {
  return (
    <section className="container-packing-3d-section" aria-label="装柜三维可视化">
      <div className="container-packing-3d-loading">三维视图加载中</div>
    </section>
  );
}
