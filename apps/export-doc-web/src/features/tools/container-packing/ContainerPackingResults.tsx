import { lazy, Suspense, useState, type MutableRefObject } from "react";
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
  const [view, setView] = useState<"plan" | "3d">("plan");
  if (!analysis) {
    return (
      <section className="container-packing-preview-placeholder" aria-label="装柜效果图状态">
        <PageState
          title="准备装柜方案"
          description="填写柜型与货物后分析，即可查看装柜图并导出现场作业单。"
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
          <div className="packing-view-switch" role="group" aria-label="装柜视图类型">
            <button type="button" className="command-button secondary" aria-pressed={view === "plan"} onClick={() => setView("plan")}>装柜图</button>
            <button type="button" className="command-button secondary" aria-pressed={view === "3d"} onClick={() => setView("3d")}>可旋转 3D</button>
            <span>从柜头向柜门装入；下层先装，上层后装</span>
          </div>
          {view === "3d" ? <Suspense fallback={<SceneLoadingState />}>
            <ContainerPackingScene3d
              analysis={analysis}
              dimensions={visualizationDimensions}
              renderMode={renderMode}
            />
          </Suspense> : <ContainerPackingVisualization
            analysis={analysis}
            dimensions={visualizationDimensions}
            renderMode={renderMode}
          />}
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
