import { FileDown, PackageCheck, Plus, RefreshCw, Trash2 } from "lucide-react";
import type { ApiContainerPackingAnalysisDto } from "../../../api/index.ts";
import {
  containerPackingRenderModeOptions,
  type ContainerPackingRenderModeValue,
} from "./containerPackingModel.ts";

type Props = {
  analysis: ApiContainerPackingAnalysisDto | null;
  autoRefreshEnabled: boolean;
  canAnalyze: boolean;
  canOperate: boolean;
  cargoCount: number;
  currentProjectId: number;
  isAnalyzing: boolean;
  pdfExportState: "idle" | "exporting";
  renderMode: ContainerPackingRenderModeValue;
  validCargoCount: number;
  onAddCargo(): void;
  onAnalyze(): void;
  onAutoRefreshChange(value: boolean): void;
  onClearCargo(): void;
  onExportPdf(): void;
  onRenderModeChange(value: ContainerPackingRenderModeValue): void;
  onScrollToResults(): void;
};

export function ContainerPackingToolbar({
  analysis,
  autoRefreshEnabled,
  canAnalyze,
  canOperate,
  cargoCount,
  currentProjectId,
  isAnalyzing,
  pdfExportState,
  renderMode,
  validCargoCount,
  onAddCargo,
  onAnalyze,
  onAutoRefreshChange,
  onClearCargo,
  onExportPdf,
  onRenderModeChange,
  onScrollToResults,
}: Props) {
  return (
    <div className="tool-panel-heading">
      <div>
        <h2>装箱分析</h2>
        <span>
          {analysis
            ? `已装 ${analysis.packedPackages} / ${analysis.totalPackages}`
            : currentProjectId > 0
              ? `方案 #${currentProjectId}`
              : `${validCargoCount} 类货物`}
        </span>
      </div>
      <div className="tool-panel-actions">
        <div className="container-packing-render-mode" aria-label="装柜显示模式">
          <span className="container-packing-render-mode-label">显示</span>
          <div className="segmented-control container-packing-render-buttons" role="group" aria-label="装柜显示模式">
            {containerPackingRenderModeOptions.map((option) => (
              <button
                key={option.value}
                className={renderMode === option.value ? "segmented-active" : ""}
                type="button"
                aria-pressed={renderMode === option.value}
                onClick={() => onRenderModeChange(option.value)}
              >
                {option.label}
              </button>
            ))}
          </div>
        </div>
        <label
          className="toggle-field container-packing-auto-refresh"
          aria-label="装柜自动刷新"
          title="开启后，停止输入约 1 秒才会自动更新结果"
        >
          <input
            type="checkbox"
            checked={autoRefreshEnabled}
            disabled={!canOperate}
            onChange={(event) => onAutoRefreshChange(event.target.checked)}
          />
          <span>自动分析</span>
        </label>
        <button className="command-button secondary" type="button" disabled={!canOperate} onClick={onAddCargo}>
          <Plus size={16} aria-hidden="true" />
          <span>添加货物</span>
        </button>
        <button
          className="command-button secondary danger"
          type="button"
          disabled={!canOperate || cargoCount === 0}
          onClick={onClearCargo}
        >
          <Trash2 size={16} aria-hidden="true" />
          <span>清空列表</span>
        </button>
        <button
          className="command-button secondary"
          type="button"
          disabled={!canAnalyze || isAnalyzing}
          onClick={onAnalyze}
        >
          <RefreshCw size={16} aria-hidden="true" />
          <span>立即刷新</span>
        </button>
        <button className="command-button secondary" type="button" disabled={!analysis} onClick={onScrollToResults}>
          <PackageCheck size={16} aria-hidden="true" />
          <span>查看效果图</span>
        </button>
        <button
          className="command-button secondary"
          type="button"
          disabled={!analysis || pdfExportState === "exporting"}
          onClick={onExportPdf}
        >
          <FileDown size={16} aria-hidden="true" />
          <span>{pdfExportState === "exporting" ? "正在生成" : "导出 PDF"}</span>
        </button>
        <button className="solid action-button" type="submit" disabled={!canAnalyze || isAnalyzing}>
          <PackageCheck size={16} aria-hidden="true" />
          <span>分析</span>
        </button>
      </div>
    </div>
  );
}
