import type { FormEvent } from "react";
import type {
  ApiContainerPackingAnalysisDto,
  ApiContainerPackingProjectSummaryDto,
  ApiContainerTypeDto,
  ExportDocManagerApiClient,
} from "../../../api/index.ts";
import { InlineNotice, PermissionNotice } from "../../../ui/PageState.tsx";
import type {
  ContainerPackingCargoRow,
  ContainerPackingFormState,
  ContainerPackingRenderModeValue,
  ContainerPackingRulesFormState,
} from "./containerPackingModel.ts";
import type { ContainerPackingVisualizationDimensions } from "./containerPackingVisualizationModel.ts";
import { ContainerPackingCargoTable } from "./ContainerPackingCargoTable.tsx";
import { ContainerPackingConfigurationPanel } from "./ContainerPackingConfigurationPanel.tsx";
import { ContainerPackingProjectPanel } from "./ContainerPackingProjectPanel.tsx";
import { ContainerPackingResults } from "./ContainerPackingResults.tsx";
import { ContainerPackingToolbar } from "./ContainerPackingToolbar.tsx";
import { useContainerPackingPdfExport } from "./useContainerPackingPdfExport.ts";

type Props = {
  client: ExportDocManagerApiClient;
  analysis: ApiContainerPackingAnalysisDto | null;
  autoRefreshEnabled: boolean;
  autoRefreshState: string;
  canAnalyze: boolean;
  canDeleteContainerType: boolean;
  canLoadProject: boolean;
  canManage: boolean;
  canOperate: boolean;
  canSaveContainerType: boolean;
  canSaveProject: boolean;
  cargoRows: ContainerPackingCargoRow[];
  container: ContainerPackingFormState;
  containerTypes: ApiContainerTypeDto[];
  currentProjectId: number;
  hasVisibleError: boolean;
  isAnalyzing: boolean;
  isDeletingContainerType: boolean;
  isDeletingProject: boolean;
  isLoadingProject: boolean;
  isRefreshingProjects: boolean;
  isSavingContainerType: boolean;
  isSavingProject: boolean;
  packingStatusText: string;
  projectName: string;
  renderMode: ContainerPackingRenderModeValue;
  rules: ContainerPackingRulesFormState;
  savedProjects: ApiContainerPackingProjectSummaryDto[];
  selectedProjectId: string;
  validCargoCount: number;
  visibleMessage: string | null;
  visualizationDimensions: ContainerPackingVisualizationDimensions | null;
  onAddCargo(): void;
  onAnalyze(): void;
  onApplyContainerType(value: string): void;
  onAutoRefreshChange(value: boolean): void;
  onClearCargo(): void;
  onContainerFieldChange(field: keyof ContainerPackingFormState, value: string): void;
  onDeleteContainerType(): void;
  onDeleteProject(): void;
  onLoadProject(): void;
  onProjectNameChange(value: string): void;
  onRefreshProjects(): void;
  onRemoveCargo(id: string): void;
  onRenderModeChange(value: ContainerPackingRenderModeValue): void;
  onRulesFieldChange<K extends keyof ContainerPackingRulesFormState>(
    field: K,
    value: ContainerPackingRulesFormState[K],
  ): void;
  onSaveContainerType(): void;
  onSaveProject(): void;
  onSelectedProjectChange(value: string): void;
  onSubmit(event: FormEvent<HTMLFormElement>): void;
  onUpdateCargo(id: string, changes: Partial<ContainerPackingCargoRow>): void;
};

export function ContainerPackingWorkspace(props: Props) {
  const pdfExport = useContainerPackingPdfExport(props.client, props.projectName, props.container, props.analysis);

  return (
    <form className="job-tool-panel" aria-label="装箱分析" onSubmit={props.onSubmit}>
      <ContainerPackingToolbar
        analysis={props.analysis}
        autoRefreshEnabled={props.autoRefreshEnabled}
        canAnalyze={props.canAnalyze}
        canOperate={props.canOperate}
        cargoCount={props.cargoRows.length}
        currentProjectId={props.currentProjectId}
        isAnalyzing={props.isAnalyzing}
        pdfExportState={pdfExport.state}
        renderMode={props.renderMode}
        validCargoCount={props.validCargoCount}
        onAddCargo={props.onAddCargo}
        onAnalyze={props.onAnalyze}
        onAutoRefreshChange={props.onAutoRefreshChange}
        onClearCargo={props.onClearCargo}
        onExportPdf={() => void pdfExport.exportPdf()}
        onRenderModeChange={props.onRenderModeChange}
        onScrollToResults={() => pdfExport.resultsRootRef.current?.scrollIntoView({ behavior: "smooth", block: "start" })}
      />

      {props.visibleMessage ? (
        <InlineNotice tone={props.hasVisibleError ? "error" : "success"}>{props.visibleMessage}</InlineNotice>
      ) : null}
      {pdfExport.message ? (
        <InlineNotice tone={pdfExport.message.kind === "error" ? "error" : "success"}>
          {pdfExport.message.text}
        </InlineNotice>
      ) : null}
      {!props.canOperate ? (
        <PermissionNotice>
          当前权限模板仅允许查看装箱方案；输入、分析、保存和柜型维护已禁用。
          {!props.canManage ? " 删除方案和自定义柜型同样需要管理权限。" : ""}
        </PermissionNotice>
      ) : null}

      <div
        className="container-packing-status-bar"
        aria-label="装柜分析状态"
        data-auto-refresh={props.autoRefreshEnabled ? "enabled" : "disabled"}
        data-auto-refresh-state={props.autoRefreshState}
      >
        <span>{props.packingStatusText}</span>
      </div>

      <ContainerPackingProjectPanel
        canLoadProject={props.canLoadProject}
        canManage={props.canManage}
        canOperate={props.canOperate}
        canSaveProject={props.canSaveProject}
        currentProjectId={props.currentProjectId}
        isDeletingProject={props.isDeletingProject}
        isLoadingProject={props.isLoadingProject}
        isRefreshingProjects={props.isRefreshingProjects}
        isSavingProject={props.isSavingProject}
        projectName={props.projectName}
        savedProjects={props.savedProjects}
        selectedProjectId={props.selectedProjectId}
        onDeleteProject={props.onDeleteProject}
        onLoadProject={props.onLoadProject}
        onProjectNameChange={props.onProjectNameChange}
        onRefreshProjects={props.onRefreshProjects}
        onSaveProject={props.onSaveProject}
        onSelectedProjectChange={props.onSelectedProjectChange}
      />

      <fieldset className="permission-fieldset" disabled={!props.canOperate}>
        <ContainerPackingConfigurationPanel
          canDeleteContainerType={props.canDeleteContainerType}
          canSaveContainerType={props.canSaveContainerType}
          container={props.container}
          containerTypes={props.containerTypes}
          isDeletingContainerType={props.isDeletingContainerType}
          isSavingContainerType={props.isSavingContainerType}
          rules={props.rules}
          onApplyContainerType={props.onApplyContainerType}
          onContainerFieldChange={props.onContainerFieldChange}
          onDeleteContainerType={props.onDeleteContainerType}
          onRulesFieldChange={props.onRulesFieldChange}
          onSaveContainerType={props.onSaveContainerType}
        />
        <ContainerPackingCargoTable
          cargoRows={props.cargoRows}
          onRemoveCargo={props.onRemoveCargo}
          onUpdateCargo={props.onUpdateCargo}
        />
      </fieldset>

      <ContainerPackingResults
        analysis={props.analysis}
        canAnalyze={props.canAnalyze}
        isAnalyzing={props.isAnalyzing}
        resultsRootRef={pdfExport.resultsRootRef}
        renderMode={props.renderMode}
        visualizationDimensions={props.visualizationDimensions}
        onAnalyze={props.onAnalyze}
      />
    </form>
  );
}
