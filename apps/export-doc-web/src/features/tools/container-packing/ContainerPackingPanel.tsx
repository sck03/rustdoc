import {
  FormEvent,
  lazy,
  Suspense,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  FolderOpen,
  PackageCheck,
  Plus,
  RefreshCw,
  Save,
  Trash2,
} from "lucide-react";
import type {
  ApiContainerPackingAnalyzeRequest,
  ApiContainerPackingAnalyzeResponse,
  ApiContainerPackingProjectDto,
} from "../../../api/index.ts";
import { ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { useConfirmation } from "../../../ui/ConfirmationProvider.tsx";
import {
  type ContainerPackingAnalyzeMode,
  type ContainerPackingAnalyzeVariables,
  type ContainerPackingCargoRow,
  type ContainerPackingFormState,
  type ContainerPackingRenderModeValue,
  type ContainerPackingRulesFormState,
  type ContainerPackingZoneValue,
  buildContainerPackingAnalyzeRequest,
  buildContainerPackingProjectSaveRequest,
  buildContainerPackingStatusText,
  containerPackingAutoRefreshDebounceMs,
  containerPackingRenderModeOptions,
  containerPackingZoneOptions,
  createContainerPackingCargoRow,
  findContainerType,
  formatFormNumber,
  formatPackingPercent,
  isValidContainerPackingCargoRow,
  normalizeContainerPackingZone,
  readNonNegativeNumberInput,
  readPositiveIntegerInput,
  readPositiveNumberInput,
  signedArgbToColorHex,
} from "./containerPackingModel.ts";
import { ContainerPackingWorkspace } from "./ContainerPackingWorkspace.tsx";

export function ContainerPackingPanel({
  client,
  canOperate,
  canManage,
}: {
  client: ExportDocManagerApiClient;
  canOperate: boolean;
  canManage: boolean;
}) {
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const [currentProjectId, setCurrentProjectId] = useState(0);
  const [currentProjectVersion, setCurrentProjectVersion] = useState(0);
  const [projectName, setProjectName] = useState("未命名方案");
  const [selectedProjectId, setSelectedProjectId] = useState("");
  const [container, setContainer] = useState<ContainerPackingFormState>({
    containerType: "20GP",
    length: "589",
    width: "235",
    height: "239",
    volume: "33.2",
    maxWeight: "28000",
  });
  const [rules, setRules] = useState<ContainerPackingRulesFormState>({
    allowRotation: true,
    usePalletConstraints: false,
    defaultPalletLength: "120",
    defaultPalletWidth: "100",
    defaultPalletHeight: "15",
    defaultPalletWeight: "25",
    enforceCenterOfGravity: false,
    centerOfGravityTolerancePercent: "20",
    minimumSupportAreaPercent: "100",
    requireSameFootprintStacking: false,
  });
  const [cargoRows, setCargoRows] = useState<ContainerPackingCargoRow[]>(() => [
    createContainerPackingCargoRow(0, {
      name: "纸箱",
      length: "60",
      width: "40",
      height: "40",
      weight: "10",
      quantity: "50",
      preferredZone: "Auto",
    }),
  ]);
  const [response, setResponse] =
    useState<ApiContainerPackingAnalyzeResponse | null>(null);
  const [analysisDimensions, setAnalysisDimensions] = useState<{
    length: number;
    width: number;
    height: number;
  } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [renderMode, setRenderMode] =
    useState<ContainerPackingRenderModeValue>("OutlineOnly");
  const [autoRefreshEnabled, setAutoRefreshEnabled] = useState(false);
  const [autoRefreshState, setAutoRefreshState] = useState<
    "idle" | "queued" | "running" | "complete" | "error" | "disabled"
  >("disabled");
  const [autoRefreshText, setAutoRefreshText] = useState("自动分析已关闭，修改后请点击“立即刷新”。");
  const autoRefreshTimerRef = useRef<number | null>(null);
  const latestAnalyzeSequenceRef = useRef(0);

  const projectsQuery = useQuery({
    queryKey: queryKeys.containerPackingProjects(),
    queryFn: () => client.listContainerPackingProjects(),
  });
  const containerTypesQuery = useQuery({
    queryKey: queryKeys.containerPackingContainerTypes(),
    queryFn: () => client.listContainerPackingContainerTypes(),
  });

  const savedProjects = projectsQuery.data?.projects ?? [];
  const containerTypes = containerTypesQuery.data?.containerTypes ?? [];
  const selectedContainerType = findContainerType(
    containerTypes,
    container.containerType,
  );
  const validCargoRows = cargoRows.filter(isValidContainerPackingCargoRow);
  const analysisRequest = useMemo(
    () => buildContainerPackingAnalyzeRequest(container, cargoRows, rules),
    [container, cargoRows, rules],
  );
  const analysisSignature = useMemo(
    () => JSON.stringify(analysisRequest),
    [analysisRequest],
  );
  const latestAnalysisSignatureRef = useRef(analysisSignature);
  latestAnalysisSignatureRef.current = analysisSignature;
  const analysis = response?.analysis ?? null;
  const visualizationDimensions = analysis ? analysisDimensions : null;
  const canAnalyze =
    canOperate &&
    readPositiveNumberInput(container.length) > 0 &&
    readPositiveNumberInput(container.width) > 0 &&
    readPositiveNumberInput(container.height) > 0 &&
    validCargoRows.length > 0;
  const canSaveProject = canOperate && canAnalyze && projectName.trim().length > 0;
  const canLoadProject = Number.parseInt(selectedProjectId, 10) > 0;
  const canSaveContainerType =
    canOperate &&
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
    rules,
    validCargoRows.length,
    autoRefreshText,
  );

  const analyzeMutation = useMutation({
    mutationFn: ({ request }: ContainerPackingAnalyzeVariables) =>
      client.analyzeContainerPacking({
        body: request,
      }),
    onSuccess: (nextResponse, variables) => {
      if (
        variables.sequence !== latestAnalyzeSequenceRef.current ||
        variables.signature !== latestAnalysisSignatureRef.current
      ) {
        return;
      }

      setResponse(nextResponse);
      setAnalysisDimensions({
        length: variables.request.container.length,
        width: variables.request.container.width,
        height: variables.request.container.height,
      });
      setAutoRefreshState("complete");
      setAutoRefreshText(
        variables.mode === "auto" ? "自动分析完成。" : "手动刷新完成。",
      );
      if (variables.mode === "manual") {
        setMessage("装箱分析完成。");
      }
    },
    onError: (error, variables) => {
      if (
        variables.sequence !== latestAnalyzeSequenceRef.current ||
        variables.signature !== latestAnalysisSignatureRef.current
      ) {
        return;
      }

      setAutoRefreshState("error");
      setAutoRefreshText("分析失败。");
      setMessage(readApiError(error));
    },
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
    const scheduledSignature = analysisSignature;
    autoRefreshTimerRef.current = window.setTimeout(() => {
      autoRefreshTimerRef.current = null;
      startContainerPackingAnalysis(
        "auto",
        scheduledRequest,
        scheduledSignature,
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
    analysisSignature,
    autoRefreshEnabled,
    canAnalyze,
    validCargoRows.length,
  ]);

  const saveProjectMutation = useMutation({
    mutationFn: () =>
      client.saveContainerPackingProject({
        body: buildContainerPackingProjectSaveRequest(
          currentProjectId,
          currentProjectVersion,
          projectName,
          container,
          cargoRows,
          rules,
        ),
      }),
    onSuccess: (nextResponse) => {
      setCurrentProjectId(nextResponse.id);
      setCurrentProjectVersion(nextResponse.project.versionNumber);
      setSelectedProjectId(String(nextResponse.id));
      setProjectName(nextResponse.project.name || "未命名方案");
      setMessage(nextResponse.message || "装柜方案已保存。");
      void queryClient.invalidateQueries({
        queryKey: queryKeys.containerPackingProjects(),
      });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const loadProjectMutation = useMutation({
    mutationFn: (projectId: number) =>
      client.getContainerPackingProject({ id: projectId }),
    onSuccess: (nextResponse) => {
      applyProjectToForm(nextResponse.project);
      setMessage("装柜方案已加载。");
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const deleteProjectMutation = useMutation({
    mutationFn: (projectId: number) =>
      client.deleteContainerPackingProject({ id: projectId }),
    onSuccess: (_, deletedProjectId) => {
      if (currentProjectId === deletedProjectId) {
        setCurrentProjectId(0);
        setCurrentProjectVersion(0);
        setProjectName("未命名方案");
      }

      setSelectedProjectId("");
      setMessage("装柜方案已删除。");
      void queryClient.invalidateQueries({
        queryKey: queryKeys.containerPackingProjects(),
      });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const saveContainerTypeMutation = useMutation({
    mutationFn: () =>
      client.saveContainerPackingContainerType({
        body: {
          id: selectedContainerType?.id ?? 0,
          name: container.containerType.trim(),
          length: readPositiveIntegerInput(container.length, 0),
          width: readPositiveIntegerInput(container.width, 0),
          height: readPositiveIntegerInput(container.height, 0),
          maxVolume: readNonNegativeNumberInput(container.volume),
          maxWeight: readNonNegativeNumberInput(container.maxWeight),
        },
      }),
    onSuccess: (nextResponse) => {
      setContainer((current) => ({
        ...current,
        containerType: nextResponse.containerType.name,
      }));
      setMessage(nextResponse.message || "柜型已保存。");
      void queryClient.invalidateQueries({
        queryKey: queryKeys.containerPackingContainerTypes(),
      });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const deleteContainerTypeMutation = useMutation({
    mutationFn: (containerTypeId: number) =>
      client.deleteContainerPackingContainerType({ id: containerTypeId }),
    onSuccess: () => {
      setContainer((current) => ({ ...current, containerType: "" }));
      setMessage("柜型已删除。");
      void queryClient.invalidateQueries({
        queryKey: queryKeys.containerPackingContainerTypes(),
      });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

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
    setResponse(null);
    setAnalysisDimensions(null);
    setMessage("货物列表已清空。");
  }

  function runContainerPackingAnalysis() {
    if (!canAnalyze) {
      return;
    }

    startContainerPackingAnalysis("manual", analysisRequest, analysisSignature);
  }

  function startContainerPackingAnalysis(
    mode: ContainerPackingAnalyzeMode,
    request: ApiContainerPackingAnalyzeRequest,
    signature: string,
  ) {
    if (!canAnalyze || signature !== latestAnalysisSignatureRef.current) {
      return;
    }

    if (autoRefreshTimerRef.current) {
      window.clearTimeout(autoRefreshTimerRef.current);
      autoRefreshTimerRef.current = null;
    }

    const sequence = latestAnalyzeSequenceRef.current + 1;
    latestAnalyzeSequenceRef.current = sequence;
    setAutoRefreshState("running");
    setAutoRefreshText(
      mode === "auto"
        ? "正在更新结果，当前视图会保持显示。"
        : "正在刷新结果，当前视图会保持显示。",
    );
    if (mode === "manual") {
      setMessage(null);
    }

    analyzeMutation.mutate({
      mode,
      request,
      sequence,
      signature,
    });
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    runContainerPackingAnalysis();
  }

  function handleSaveProject() {
    if (!canSaveProject || saveProjectMutation.isPending) {
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

  async function handleDeleteProject() {
    if (!canManage) {
      return;
    }

    const projectId = Number.parseInt(
      selectedProjectId || String(currentProjectId),
      10,
    );
    if (projectId <= 0 || deleteProjectMutation.isPending) {
      return;
    }

    const selectedProject = savedProjects.find(
      (project) => project.id === projectId,
    );
    const deleteProjectName =
      selectedProject?.name ||
      (projectId === currentProjectId ? projectName : "");
    const confirmed = await requestConfirmation({
      title: "删除装柜方案",
      description: deleteProjectName.trim() ? `确定删除方案“${deleteProjectName.trim()}”吗？` : "确定删除当前装柜方案吗？",
      confirmLabel: "确认删除",
      tone: "danger",
    });
    if (!confirmed) {
      return;
    }

    setMessage(null);
    deleteProjectMutation.mutate(projectId);
  }

  function handleSaveContainerType() {
    if (!canSaveContainerType || saveContainerTypeMutation.isPending) {
      return;
    }

    setMessage(null);
    saveContainerTypeMutation.mutate();
  }

  function handleDeleteContainerType() {
    if (
      !selectedContainerType ||
      selectedContainerType.isSystemDefault ||
      deleteContainerTypeMutation.isPending
    ) {
      return;
    }

    setMessage(null);
    deleteContainerTypeMutation.mutate(selectedContainerType.id);
  }

  function applyProjectToForm(project: ApiContainerPackingProjectDto) {
    setCurrentProjectId(project.id);
    setCurrentProjectVersion(project.versionNumber);
    setProjectName(project.name || "未命名方案");
    setSelectedProjectId(String(project.id));
    setContainer({
      containerType: project.containerType || "",
      length: String(project.container.length || ""),
      width: String(project.container.width || ""),
      height: String(project.container.height || ""),
      volume: formatFormNumber(project.container.volume),
      maxWeight: formatFormNumber(project.container.maxWeight),
    });
    setRules({
      allowRotation: project.rules.allowRotation,
      usePalletConstraints: project.rules.usePalletConstraints,
      defaultPalletLength: String(project.rules.defaultPalletLength || 120),
      defaultPalletWidth: String(project.rules.defaultPalletWidth || 100),
      defaultPalletHeight: String(project.rules.defaultPalletHeight ?? 15),
      defaultPalletWeight: formatFormNumber(project.rules.defaultPalletWeight),
      enforceCenterOfGravity: project.rules.enforceCenterOfGravity,
      centerOfGravityTolerancePercent: formatFormNumber(
        project.rules.centerOfGravityTolerancePercent,
      ),
      minimumSupportAreaPercent: formatFormNumber(
        project.rules.minimumSupportAreaPercent,
      ),
      requireSameFootprintStacking: project.rules.requireSameFootprintStacking,
    });
    setCargoRows(
      project.cargoItems.length > 0
        ? project.cargoItems.map((item, index) =>
            createContainerPackingCargoRow(index, {
              name: item.name || `货物 ${index + 1}`,
              length: formatFormNumber(item.length),
              width: formatFormNumber(item.width),
              height: formatFormNumber(item.height),
              weight: formatFormNumber(item.weight),
              quantity: String(item.quantity || 1),
              colorHex: signedArgbToColorHex(item.colorArgb),
              usePallet: item.usePallet,
              unitsPerPallet: String(item.unitsPerPallet || 1),
              maxTopLoadWeight: formatFormNumber(item.maxTopLoadWeight),
              preferredZone: normalizeContainerPackingZone(item.preferredZone),
              loadSequence: String(item.loadSequence || index + 1),
              priorityGroup: item.priorityGroup || "",
            }),
          )
        : [createContainerPackingCargoRow(0)],
    );
    setResponse(null);
    setAnalysisDimensions(null);
  }

  const queryMessage = projectsQuery.isError
    ? readApiError(projectsQuery.error)
    : containerTypesQuery.isError
      ? readApiError(containerTypesQuery.error)
      : null;
  const visibleMessage = message ?? queryMessage;
  const hasVisibleError =
    Boolean(queryMessage) ||
    analyzeMutation.isError ||
    saveProjectMutation.isError ||
    loadProjectMutation.isError ||
    deleteProjectMutation.isError ||
    saveContainerTypeMutation.isError ||
    deleteContainerTypeMutation.isError;

  return (
    <ContainerPackingWorkspace
      analysis={analysis} autoRefreshEnabled={autoRefreshEnabled} autoRefreshState={autoRefreshState}
      canAnalyze={canAnalyze} canDeleteContainerType={canDeleteContainerType} canLoadProject={canLoadProject}
      canManage={canManage} canOperate={canOperate}
      canSaveContainerType={canSaveContainerType} canSaveProject={canSaveProject} cargoRows={cargoRows}
      container={container} containerTypes={containerTypes} currentProjectId={currentProjectId}
      hasVisibleError={hasVisibleError} isAnalyzing={analyzeMutation.isPending} isDeletingContainerType={deleteContainerTypeMutation.isPending}
      isDeletingProject={deleteProjectMutation.isPending} isLoadingProject={loadProjectMutation.isPending}
      isRefreshingProjects={projectsQuery.isFetching} isSavingContainerType={saveContainerTypeMutation.isPending}
      isSavingProject={saveProjectMutation.isPending} onAddCargo={addCargoRow} onAnalyze={runContainerPackingAnalysis}
      onApplyContainerType={applyContainerType} onAutoRefreshChange={setAutoRefreshEnabled} onClearCargo={clearCargoRows}
      onContainerFieldChange={updateContainerField} onDeleteContainerType={handleDeleteContainerType} onDeleteProject={handleDeleteProject}
      onLoadProject={handleLoadProject} onProjectNameChange={updateProjectName} onRefreshProjects={() => void queryClient.invalidateQueries({ queryKey: queryKeys.containerPackingProjects() })}
      onRemoveCargo={removeCargoRow} onRenderModeChange={setRenderMode} onRulesFieldChange={updateRulesField}
      onSaveContainerType={handleSaveContainerType} onSaveProject={handleSaveProject} onSelectedProjectChange={setSelectedProjectId}
      onSubmit={handleSubmit} onUpdateCargo={updateCargoRow} packingStatusText={packingStatusText} projectName={projectName}
      renderMode={renderMode} rules={rules} savedProjects={savedProjects} selectedProjectId={selectedProjectId}
      validCargoCount={validCargoRows.length} visibleMessage={visibleMessage}
      visualizationDimensions={visualizationDimensions}
    />
  );
}
