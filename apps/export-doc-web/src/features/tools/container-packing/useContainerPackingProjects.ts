import { useRef, useState, type SetStateAction } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { useConfirmation } from "../../../ui/ConfirmationProvider.tsx";
import { useUnsavedChangesGuard } from "../../../ui/unsavedChangesGuard.tsx";
import { useScopedMutation } from "../../../ui/useScopedMutation.ts";
import { useAbortableOperationController } from "../../../ui/useAbortableOperation.ts";
import { mergeSavedDraftFields } from "../../../ui/recordDraftMerge.ts";
import { buildContainerPackingProjectSaveRequest } from "./containerPackingModel.ts";
import {
  createPackingProjectDraft,
  packingDraftFromProject,
  packingDraftFields,
  type ContainerPackingProjectDraft,
} from "./containerPackingProjectDraft.ts";

type ProjectCommand = { kind: "save" } | { kind: "delete"; projectId: number };

export function useContainerPackingProjects(
  client: ExportDocManagerApiClient,
  resetAnalysis: () => void,
  setMessage: (message: string | null) => void,
) {
  const [state, setState] = useState(() => {
    const draft = createPackingProjectDraft();
    return { id: 0, version: 0, draft, baseline: draft };
  });
  const latest = useRef(state);
  latest.current = state;
  const [failed, setFailed] = useState(false);
  const [selectedProjectId, setSelected] = useState("");
  const selected = useRef(selectedProjectId);
  selected.current = selectedProjectId;
  const confirmAction = useConfirmation();
  const loads = useAbortableOperationController(client, {
    cancelPrevious: true,
  });
  const isDirty = JSON.stringify(state.draft) !== JSON.stringify(state.baseline);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty,
    message: "装柜方案有未保存的修改。",
  });
  const projectsQuery = useQuery({
    queryKey: queryKeys.containerPackingProjects(),
    queryFn: ({ signal }) => client.listContainerPackingProjects({ limit: 100 }, { signal }),
  });
  const update = (next: typeof state) => {
    latest.current = next;
    setState(next);
  };
  const select = (value: string) => {
    selected.current = value;
    setSelected(value);
  };
  const success = (message: string) => {
    setFailed(false);
    setMessage(message);
  };
  const onError = (error: unknown) => {
    setFailed(true);
    setMessage(readApiError(error));
  };

  function field<K extends keyof ContainerPackingProjectDraft>(
    key: K,
    value: SetStateAction<ContainerPackingProjectDraft[K]>,
  ) {
    loads.cancel();
    const current = latest.current;
    const next =
      typeof value === "function"
        ? (value as (value: ContainerPackingProjectDraft[K]) => ContainerPackingProjectDraft[K])(current.draft[key])
        : value;
    update({ ...current, draft: { ...current.draft, [key]: next } });
  }

  const command = useScopedMutation(`packing:${state.id}`, client, {
    mutationFn: async (input: ProjectCommand, signal, { checkCurrent, continueInScope }) => {
      setFailed(false);
      if (input.kind === "save") {
        loads.cancel();
        const submitted = latest.current;
        const { projectName, container, cargoRows, rules } = submitted.draft;
        const response = await client.saveContainerPackingProject(
          {
            body: buildContainerPackingProjectSaveRequest(
              submitted.id,
              submitted.version,
              projectName,
              container,
              cargoRows,
              rules,
            ),
          },
          { signal },
        );
        checkCurrent();
        const baseline = packingDraftFromProject(response.project);
        const draft = mergeSavedDraftFields(latest.current.draft, submitted.draft, baseline, packingDraftFields);
        continueInScope(`packing:${response.id}`);
        update({
          id: response.id,
          version: response.project.versionNumber,
          draft,
          baseline,
        });
        select(String(response.id));
        success(response.message || "装柜方案已保存。");
      } else {
        const project = projectsQuery.data?.projects?.find((project) => project.id === input.projectId);
        if (
          !(await confirmAction({
            title: "删除装柜方案",
            description: `确定删除方案“${project?.name || latest.current.draft.projectName}”吗？`,
            confirmLabel: "确认删除",
            tone: "danger",
          }))
        )
          return false;
        checkCurrent();
        loads.cancel();
        const expectedVersion = latest.current.id === input.projectId ? latest.current.version : project?.versionNumber;
        if (!expectedVersion) throw new Error("方案版本已丢失，请重新加载后重试。");
        await client.deleteContainerPackingProject({ id: input.projectId, expectedVersion }, { signal });
        checkCurrent();
        if (latest.current.id === input.projectId) {
          continueInScope("packing:0");
          // Keep the cargo draft so a deleted project can be saved under a new identity.
          update({
            ...latest.current,
            id: 0,
            version: 0,
            baseline: createPackingProjectDraft(),
          });
        }
        select("");
        success("装柜方案已删除。");
      }
      return true;
    },
    refreshKeys: (changed) => (changed ? [queryKeys.containerPackingProjects()] : []),
    onRefreshError: (error) => onError(new Error(`操作已完成，但方案目录刷新失败：${readApiError(error)}`)),
    onError,
  });
  const load = useScopedMutation("packing-load", client, {
    mutationFn: (projectId: number) =>
      loads.run(async (signal) => {
        const submitted = latest.current;
        if (!(await confirmDiscardChanges("加载其他方案"))) return;
        signal.throwIfAborted();
        if (latest.current !== submitted || selected.current !== String(projectId)) return;
        command.cancel();
        const response = await client.getContainerPackingProject({ id: projectId }, { signal });
        signal.throwIfAborted();
        const draft = packingDraftFromProject(response.project);
        update({
          id: response.project.id,
          version: response.project.versionNumber,
          draft,
          baseline: draft,
        });
        select(String(response.project.id));
        resetAnalysis();
        success("装柜方案已加载。");
      }),
    onError,
  });
  return {
    ...state.draft,
    currentProjectId: state.id,
    currentProjectVersion: state.version,
    selectedProjectId,
    projectsQuery,
    isSaving: command.isPending,
    saveProjectMutation: {
      mutate: () => command.mutate({ kind: "save" }),
      isPending: command.isPending,
      isError: failed,
    },
    deleteProjectMutation: {
      mutate: (projectId: number) => command.mutate({ kind: "delete", projectId }),
      isPending: command.isPending,
      isError: failed,
    },
    loadProjectMutation: { ...load, isError: failed },
    setProjectName: (value: SetStateAction<string>) => field("projectName", value),
    setContainer: (value: SetStateAction<ContainerPackingProjectDraft["container"]>) => field("container", value),
    setRules: (value: SetStateAction<ContainerPackingProjectDraft["rules"]>) => field("rules", value),
    setCargoRows: (value: SetStateAction<ContainerPackingProjectDraft["cargoRows"]>) => field("cargoRows", value),
    setSelectedProjectId: (value: string) => {
      loads.cancel();
      select(value);
    },
  };
}
