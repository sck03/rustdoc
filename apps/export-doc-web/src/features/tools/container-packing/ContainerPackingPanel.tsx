import {
  FormEvent,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { useQueryClient } from "@tanstack/react-query";
import type {
  ApiContainerPackingAnalyzeRequest,
} from "../../../api/index.ts";
import { ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { useConfirmation } from "../../../ui/ConfirmationProvider.tsx";
import { useScopedConfirmation } from "../../../ui/useScopedConfirmation.ts";
import { useTransientPreview } from "../../../ui/useTransientPreview.ts";
import {
  type ContainerPackingAnalyzeMode,
  type ContainerPackingCargoRow,
  type ContainerPackingFormState,
  type ContainerPackingRenderModeValue,
  type ContainerPackingRulesFormState,
  buildContainerPackingAnalyzeRequest,
  buildContainerPackingStatusText,
  containerPackingAutoRefreshDebounceMs,
  createContainerPackingCargoRow,
  findContainerType,
  formatFormNumber,
  isValidContainerPackingCargoRow,
  readPositiveIntegerInput,
  readPositiveNumberInput,
} from "./containerPackingModel.ts";
import { ContainerPackingWorkspace } from "./ContainerPackingWorkspace.tsx";
import { useContainerPackingProjects } from "./useContainerPackingProjects.ts";
import { useContainerPackingTypes } from "./useContainerPackingTypes.ts";
import type { ContainerPackingSnapshot } from "./containerPackingSnapshot.ts";

export function ContainerPackingPanel({
  client,
  canOperate,
  canManage,
}: {
  client: ExportDocManagerApiClient;
  canOperate: boolean;
  canManage: boolean;
}) {
  const requestConfirmation = useScopedConfirmation(client, useConfirmation());
  const queryClient = useQueryClient();
  const [snapshot, setSnapshot] = useState<ContainerPackingSnapshot | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [renderMode, setRenderMode] =
    useState<ContainerPackingRenderModeValue>("FullGrid");
  const [autoRefreshEnabled, setAutoRefreshEnabled] = useState(false);
  const [autoRefreshState, setAutoRefreshState] = useState<
    "idle" | "queued" | "running" | "complete" | "error" | "disabled"
  >("disabled");
  const [autoRefreshText, setAutoRefreshText] = useState("自动分析已关闭，修改后请点击“立即刷新”。");
  const autoRefreshTimerRef = useRef<number | null>(null);
  useEffect(() => { setSnapshot(null); }, [client]);

  const {
    currentProjectId, projectName, selectedProjectId, container, rules, cargoRows,
    setProjectName, setSelectedProjectId, setContainer, setRules, setCargoRows,
    projectsQuery, directory, saveProjectMutation, loadProjectMutation, deleteProjectMutation, isSaving,
  } = useContainerPackingProjects(client, () => setSnapshot(null), setMessage);
  const typeWorkspace = useContainerPackingTypes(client, currentProjectId, container, setContainer, canManage && canOperate, setMessage);
  const { query: containerTypesQuery, types: containerTypes, selected: selectedContainerType } = typeWorkspace;

  const savedProjects = projectsQuery.data?.projects ?? [];
  const validCargoRows = cargoRows.filter(isValidContainerPackingCargoRow);
  const analysisRequest = useMemo(
    () => buildContainerPackingAnalyzeRequest(container, cargoRows, rules),
    [container, cargoRows, rules],
  );
  const latestAnalysisRequestRef = useRef(analysisRequest);
  latestAnalysisRequestRef.current = analysisRequest;
  const analysis = snapshot?.response.analysis ?? null;
  const dimensions = snapshot?.request.container;
  const visualizationDimensions = useMemo(() => analysis && dimensions ? {
    length: dimensions.length ?? 0, width: dimensions.width ?? 0, height: dimensions.height ?? 0,
  } : null, [analysis, dimensions]);
  const exportSnapshot = snapshot?.request === analysisRequest && canOperate ? snapshot : null;
  const canAnalyze =
    canOperate &&
    readPositiveNumberInput(container.length) > 0 &&
    readPositiveNumberInput(container.width) > 0 &&
    readPositiveNumberInput(container.height) > 0 &&
    validCargoRows.length > 0;
  const canSaveProject = canOperate && canAnalyze && projectName.trim().length > 0;
  const canLoadProject = Number.parseInt(selectedProjectId, 10) > 0;
  const canSaveContainerType =
    canManage &&
    container.containerType.trim().length > 0 &&
    readPositiveIntegerInput(container.length, 0) > 0 &&
    readPositiveIntegerInput(container.width, 0) > 0 &&
    readPositiveIntegerInput(container.height, 0) > 0 &&
    selectedContainerType?.isSystemDefault !== true;
  const canDeleteContainerType = Boolean(
    canManage && selectedContainerType && !selectedContainerType.isSystemDefault,
  );
  const packingStatusText = buildContainerPackingStatusText(
    analysis,
    snapshot?.rules ?? rules,
    validCargoRows.length,
    analysis && !exportSnapshot ? "输入已修改，请重新分析后导出。" : autoRefreshText,
  );

  const analysisScope = useMemo(() => ({}), [client, analysisRequest, canOperate]);
  const analyze = useTransientPreview(analysisScope,
    async (signal, variables: { request: ApiContainerPackingAnalyzeRequest; mode: ContainerPackingAnalyzeMode }) => ({
      nextResponse: await client.analyzeContainerPacking({ body: variables.request }, { signal }), variables,
    }),
    error => { setAutoRefreshState("error"); setAutoRefreshText("分析失败。"); setMessage(readApiError(error)); },
    ({ nextResponse, variables }) => {
      setSnapshot({ request: variables.request, response: nextResponse, containerType: container.containerType, rules });
      setAutoRefreshState("complete");
      setAutoRefreshText(
        variables.mode === "auto" ? "自动分析完成。" : "手动刷新完成。",
      );
      if (variables.mode === "manual") {
        setMessage("装箱分析完成。");
      }
    });

  useEffect(() => {
    if (autoRefreshTimerRef.current) {
      window.clearTimeout(autoRefreshTimerRef.current);
      autoRefreshTimerRef.current = null;
    }

    if (!autoRefreshEnabled) {
      setAutoRefreshState("disabled");
      setAutoRefreshText("自动分析已关闭，修改后请点击“立即刷新”。");
      return undefined;
    }

    if (!canAnalyze) {
      setAutoRefreshState("idle");
      setAutoRefreshText(
        validCargoRows.length > 0 ? "等待有效柜型。" : "等待有效货物。",
      );
      return undefined;
    }

    setAutoRefreshState("queued");
    setAutoRefreshText("内容已修改，将在停止输入后自动刷新。");
    const scheduledRequest = analysisRequest;
    autoRefreshTimerRef.current = window.setTimeout(() => {
      autoRefreshTimerRef.current = null;
      startContainerPackingAnalysis(
        "auto",
        scheduledRequest,
      );
    }, containerPackingAutoRefreshDebounceMs);

    return () => {
      if (autoRefreshTimerRef.current) {
        window.clearTimeout(autoRefreshTimerRef.current);
        autoRefreshTimerRef.current = null;
      }
    };
  }, [
    analysisRequest,
    autoRefreshEnabled,
    canAnalyze,
    validCargoRows.length, client,
  ]);

  function markAnalysisInputsChanged() {
    setMessage(null);
  }

  function updateProjectName(value: string) {
    setProjectName(value);
    setMessage(null);
  }

  function updateContainerField(
    field: keyof ContainerPackingFormState,
    value: string,
  ) {
    setContainer((current) => ({ ...current, [field]: value }));
    markAnalysisInputsChanged();
  }

  function applyContainerType(typeName: string) {
    const definition = findContainerType(containerTypes, typeName);
    setContainer((current) =>
      definition
        ? {
            ...current,
            containerType: definition.name,
            length: String(definition.length),
            width: String(definition.width),
            height: String(definition.height),
            volume: formatFormNumber(definition.maxVolume),
            maxWeight: formatFormNumber(definition.maxWeight),
          }
        : { ...current, containerType: typeName },
    );
    markAnalysisInputsChanged();
  }

  function updateRulesField<K extends keyof ContainerPackingRulesFormState>(
    field: K,
    value: ContainerPackingRulesFormState[K],
  ) {
    setRules((current) => ({ ...current, [field]: value }));
    markAnalysisInputsChanged();
  }

  function updateCargoRow(
    id: string,
    changes: Partial<ContainerPackingCargoRow>,
  ) {
    setCargoRows((current) =>
      current.map((row) => (row.id === id ? { ...row, ...changes } : row)),
    );
    markAnalysisInputsChanged();
  }

  function addCargoRow() {
    setCargoRows((current) => [
      ...current,
      createContainerPackingCargoRow(current.length),
    ]);
    markAnalysisInputsChanged();
  }

  function removeCargoRow(id: string) {
    setCargoRows((current) => current.filter((row) => row.id !== id));
    markAnalysisInputsChanged();
  }

  async function clearCargoRows() {
    if (cargoRows.length === 0) {
      return;
    }

    if (!await requestConfirmation({ title: "清空货物列表", description: "确定清空当前装柜方案中的所有货物吗？", details: ["尚未保存的装载分析结果也会被清空。"], confirmLabel: "确认清空", tone: "danger" })) {
      return;
    }

    setCargoRows([]);
    setSnapshot(null);
    setMessage("货物列表已清空。");
  }

  function runContainerPackingAnalysis() {
    if (!canAnalyze) {
      return;
    }

    startContainerPackingAnalysis("manual", analysisRequest);
  }

  function startContainerPackingAnalysis(
    mode: ContainerPackingAnalyzeMode,
    request: ApiContainerPackingAnalyzeRequest,
  ) {
    if (!canAnalyze || request !== latestAnalysisRequestRef.current) {
      return;
    }

    if (autoRefreshTimerRef.current) {
      window.clearTimeout(autoRefreshTimerRef.current);
      autoRefreshTimerRef.current = null;
    }

    setAutoRefreshState("running");
    setAutoRefreshText(
      mode === "auto"
        ? "正在更新结果，当前视图会保持显示。"
        : "正在刷新结果，当前视图会保持显示。",
    );
    if (mode === "manual") {
      setMessage(null);
    }

    void analyze.generate({ mode, request });
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    runContainerPackingAnalysis();
  }

  function handleSaveProject() {
    if (!canSaveProject || isSaving) {
      return;
    }

    setMessage(null);
    saveProjectMutation.mutate();
  }

  function handleLoadProject() {
    const projectId = Number.parseInt(selectedProjectId, 10);
    if (projectId <= 0 || loadProjectMutation.isPending) {
      return;
    }

    setMessage(null);
    loadProjectMutation.mutate(projectId);
  }

  function handleDeleteProject() {
    const projectId = Number.parseInt(selectedProjectId || String(currentProjectId), 10);
    if (canManage && projectId > 0 && !isSaving) deleteProjectMutation.mutate(projectId);
  }

  function handleSaveContainerType() {
    if (!canSaveContainerType || typeWorkspace.command.isPending) {
      return;
    }

    setMessage(null);
    typeWorkspace.command.mutate("save");
  }

  function handleDeleteContainerType() {
    if (
      !selectedContainerType ||
      selectedContainerType.isSystemDefault ||
      typeWorkspace.command.isPending
    ) {
      return;
    }

    setMessage(null);
    typeWorkspace.command.mutate("delete");
  }

  const queryMessage = projectsQuery.isError
    ? readApiError(projectsQuery.error)
    : containerTypesQuery.isError
      ? readApiError(containerTypesQuery.error)
      : null;
  const visibleMessage = message ?? queryMessage;
  const hasVisibleError =
    Boolean(queryMessage) ||
    autoRefreshState === "error" ||
    saveProjectMutation.isError ||
    loadProjectMutation.isError ||
    deleteProjectMutation.isError ||
    typeWorkspace.failed;

  return (
    <ContainerPackingWorkspace
      analysis={analysis} autoRefreshEnabled={autoRefreshEnabled} autoRefreshState={autoRefreshState} client={client}
      canAnalyze={canAnalyze} canDeleteContainerType={canDeleteContainerType} canLoadProject={canLoadProject}
      canManage={canManage} canOperate={canOperate}
      canSaveContainerType={canSaveContainerType} canSaveProject={canSaveProject} cargoRows={cargoRows}
      container={container} containerTypes={containerTypes} currentProjectId={currentProjectId}
      hasVisibleError={hasVisibleError} isAnalyzing={analyze.isPending} isDeletingContainerType={typeWorkspace.command.isPending}
      isDeletingProject={deleteProjectMutation.isPending} isLoadingProject={loadProjectMutation.isPending}
      isRefreshingProjects={projectsQuery.isFetching} isSavingContainerType={typeWorkspace.command.isPending}
      isSavingProject={isSaving} onAddCargo={addCargoRow} onAnalyze={runContainerPackingAnalysis}
      onApplyContainerType={applyContainerType} onAutoRefreshChange={setAutoRefreshEnabled} onClearCargo={clearCargoRows}
      onContainerFieldChange={updateContainerField} onDeleteContainerType={handleDeleteContainerType} onDeleteProject={handleDeleteProject}
      onLoadProject={handleLoadProject} onProjectNameChange={updateProjectName} onRefreshProjects={() => void queryClient.invalidateQueries({ queryKey: queryKeys.containerPackingProjects() })}
      onRemoveCargo={removeCargoRow} onRenderModeChange={setRenderMode} onRulesFieldChange={updateRulesField}
      onSaveContainerType={handleSaveContainerType} onSaveProject={handleSaveProject} onSelectedProjectChange={setSelectedProjectId}
      onSubmit={handleSubmit} onUpdateCargo={updateCargoRow} packingStatusText={packingStatusText} projectName={projectName}
      renderMode={renderMode} rules={rules} savedProjects={savedProjects} selectedProjectId={selectedProjectId}
      validCargoCount={validCargoRows.length} visibleMessage={visibleMessage}
      visualizationDimensions={visualizationDimensions}
      exportSnapshot={exportSnapshot}
      directory={directory}
    />
  );
}
