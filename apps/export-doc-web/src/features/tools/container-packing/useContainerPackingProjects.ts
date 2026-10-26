import { useRef, useState, type SetStateAction } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { useConfirmation } from "../../../ui/ConfirmationProvider.tsx";
import { useUnsavedChangesGuard } from "../../../ui/unsavedChangesGuard.tsx";
import { useScopedSave } from "../../../ui/useScopedSave.ts";
import { isAbortError, useAbortableOperationController } from "../../../ui/useAbortableOperation.ts";
import { mergeSavedDraftFields } from "../../../ui/recordDraftMerge.ts";
import { buildContainerPackingProjectSaveRequest } from "./containerPackingModel.ts";
import { createPackingProjectDraft, packingDraftFromProject, packingDraftFields, type ContainerPackingProjectDraft } from "./containerPackingProjectDraft.ts";

export function useContainerPackingProjects(client: ExportDocManagerApiClient, resetAnalysis: () => void, setMessage: (message: string | null) => void) {
  const [state, setState] = useState(() => {
    const draft = createPackingProjectDraft(); return { id: 0, version: 0, draft, baseline: draft };
  });
  const latest = useRef(state); latest.current = state;
  const [selectedProjectId, setSelected] = useState("");
  const selected = useRef(selectedProjectId); selected.current = selectedProjectId;
  const queries = useQueryClient(), confirmAction = useConfirmation();
  const boundary = useScopedSave(`packing:${state.id}`, client);
  const loads = useAbortableOperationController(client, { cancelPrevious: true });
  const isDirty = JSON.stringify(state.draft) !== JSON.stringify(state.baseline);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty, message: "装柜方案有未保存的修改。" });
  const projectsQuery = useQuery({ queryKey: queryKeys.containerPackingProjects(), queryFn: ({ signal }) => client.listContainerPackingProjects({ limit: 100 }, { signal }) });
  const update = (next: typeof state) => { latest.current = next; setState(next); };
  function field<K extends keyof ContainerPackingProjectDraft>(key: K, value: SetStateAction<ContainerPackingProjectDraft[K]>) {
    loads.cancel();
    const current = latest.current;
    const next = typeof value === "function" ? (value as (value: ContainerPackingProjectDraft[K]) => ContainerPackingProjectDraft[K])(current.draft[key]) : value;
    update({ ...current, draft: { ...current.draft, [key]: next } });
  }
  const onError = (error: unknown) => { if (!isAbortError(error)) setMessage(readApiError(error)); };
  async function refresh(check: () => void) {
    try { await queries.invalidateQueries({ queryKey: queryKeys.containerPackingProjects() }); }
    catch (error) { check(); setMessage(`操作已完成，但方案目录刷新失败：${readApiError(error)}`); }
  }
  const saveProjectMutation = useMutation({
    mutationFn: () => boundary.run(async ({ signal, checkCurrent, continueInScope }) => {
      loads.cancel();
      const submitted = latest.current;
      const { projectName, container, cargoRows, rules } = submitted.draft;
      const response = await client.saveContainerPackingProject({ body: buildContainerPackingProjectSaveRequest(submitted.id, submitted.version, projectName, container, cargoRows, rules) }, { signal });
      checkCurrent();
      const baseline = packingDraftFromProject(response.project);
      const draft = mergeSavedDraftFields(latest.current.draft, submitted.draft, baseline, packingDraftFields);
      continueInScope(`packing:${response.id}`);
      update({ id: response.id, version: response.project.versionNumber, draft, baseline });
      setSelected(String(response.id)); selected.current = String(response.id);
      setMessage(response.message || "装柜方案已保存。");
      await refresh(checkCurrent);
    }), onError,
  });
  const loadProjectMutation = useMutation({
    mutationFn: (projectId: number) => loads.run(async signal => {
      const submitted = latest.current;
      if (!await confirmDiscardChanges("加载其他方案")) return;
      signal.throwIfAborted();
      if (latest.current !== submitted || selected.current !== String(projectId)) return;
      boundary.cancel();
      const response = await client.getContainerPackingProject({ id: projectId }, { signal });
      signal.throwIfAborted();
      const draft = packingDraftFromProject(response.project);
      update({ id: response.project.id, version: response.project.versionNumber, draft, baseline: draft });
      setSelected(String(response.project.id)); selected.current = String(response.project.id);
      resetAnalysis(); setMessage("装柜方案已加载。");
    }), onError,
  });
  const deleteProjectMutation = useMutation({
    mutationFn: (projectId: number) => boundary.run(async ({ signal, checkCurrent, continueInScope }) => {
      const project = projectsQuery.data?.projects?.find(project => project.id === projectId);
      if (!await confirmAction({ title: "删除装柜方案", description: `确定删除方案“${project?.name || latest.current.draft.projectName}”吗？`, confirmLabel: "确认删除", tone: "danger" })) return;
      checkCurrent(); loads.cancel();
      await client.deleteContainerPackingProject({ id: projectId }, { signal }); checkCurrent();
      if (latest.current.id === projectId) {
        continueInScope("packing:0");
        // Keep the cargo draft so a deleted project can be saved under a new identity.
        update({ ...latest.current, id: 0, version: 0, baseline: createPackingProjectDraft() });
      }
      setSelected(""); selected.current = ""; setMessage("装柜方案已删除。"); await refresh(checkCurrent);
    }), onError,
  });
  return {
    ...state.draft, currentProjectId: state.id, currentProjectVersion: state.version, selectedProjectId,
    projectsQuery, saveProjectMutation, loadProjectMutation, deleteProjectMutation, isSaving: boundary.isSaving,
    setProjectName: (value: SetStateAction<string>) => field("projectName", value),
    setContainer: (value: SetStateAction<ContainerPackingProjectDraft["container"]>) => field("container", value),
    setRules: (value: SetStateAction<ContainerPackingProjectDraft["rules"]>) => field("rules", value),
    setCargoRows: (value: SetStateAction<ContainerPackingProjectDraft["cargoRows"]>) => field("cargoRows", value),
    setSelectedProjectId: (value: string) => { loads.cancel(); selected.current = value; setSelected(value); },
  };
}
