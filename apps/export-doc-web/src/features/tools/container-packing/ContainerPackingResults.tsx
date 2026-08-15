import { lazy, Suspense, type MutableRefObject } from "react";
import type { ApiContainerPackingAnalysisDto } from "../../../api/index.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";
import { PageState } from "../../../ui/PageState.tsx";
import {
  formatPackingPercent,
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
  isAnalyzing: boolean;
  resultsRootRef: MutableRefObject<HTMLDivElement | null>;
  renderMode: ContainerPackingRenderModeValue;
  visualizationDimensions: ContainerPackingVisualizationDimensions | null;
  onAnalyze(): void;
};

export function ContainerPackingResults({
  analysis,
  canAnalyze,
  isAnalyzing,
  resultsRootRef,
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
      ref={(element) => { resultsRootRef.current = element; }}
    >
      <PackingSummary analysis={analysis} />
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

function SceneLoadingState() {
  return (
    <section className="container-packing-3d-section" aria-label="装柜三维可视化">
      <div className="container-packing-3d-loading">三维视图加载中</div>
    </section>
  );
}
