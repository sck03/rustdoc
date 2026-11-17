import { useEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiPermissionTemplateDto, ApiPermissionTemplateSaveRequest, ApiPermissionTemplateCatalogResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { identityQueryRoots, queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { combinePermissionSchemes, createSchemeFromEmployee, schemeStartingPoints, toPermissionGrants, type SchemeStartingPoint } from "./permissionSchemeModel.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useAbortableOperationController, isAbortError } from "../../ui/useAbortableOperation.ts";
import { createEmptyDraft, createDraftFromTemplate, createCustomTemplateCode, buildTemplateDraftSnapshot, type TemplateDraft } from "./permissionTemplateDraft.ts";

export function usePermissionTemplateWorkspace(client: ExportDocManagerApiClient, canManageUsers: boolean) {
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<TemplateDraft>(() => createEmptyDraft());
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [persistedDraftSnapshot, setPersistedDraftSnapshot] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [startingPoint, setStartingPoint] = useState<SchemeStartingPoint>("blank");
  const [sourceIds, setSourceIds] = useState<number[]>([]);
  const [creating, setCreating] = useState(false);
  const [publicationBusy, setPublicationBusy] = useState(false);

  const catalogQuery = useQuery({
    queryKey: queryKeys.permissionTemplates(),
    queryFn: ({ signal }) => client.listPermissionTemplates({ signal }),
    enabled: canManageUsers,
  });
  const templates = catalogQuery.data?.templates ?? [];
  const resources = catalogQuery.data?.resources ?? [];
  const assignableResources = useMemo(() => resources.filter((resource) => !resource.isTechnical), [resources]);
  const technicalResources = useMemo(() => resources.filter((resource) => resource.isTechnical), [resources]);
  const resourceByKey = useMemo(() => new Map(resources.map((resource) => [resource.key, resource])), [resources]);
  const selectedTemplate = templates.find((template) => template.id === selectedId);

  useEffect(() => {
    if (selectedId == null && templates.length > 0) applyTemplate(templates[0]);
  }, [selectedId, templates]);

  useEffect(() => {
    if (!catalogQuery.isError) return;
    setMessage(readApiError(catalogQuery.error));
    setSuccessMessage(null);
  }, [catalogQuery.error, catalogQuery.isError]);

  const commandOwner = useMemo(() => ({ client, canManageUsers }), [client, canManageUsers]);
  const command = useScopedMutation(`permission-template:${selectedId}`, commandOwner, {
    mutationFn: async (input: { kind: "save"; body: ApiPermissionTemplateSaveRequest } | { kind: "delete"; id: number; expectedVersion: number }, signal, { checkCurrent, continueInScope }) => {
      if (!canManageUsers) return false;
      if (input.kind === "delete") {
        await client.deletePermissionTemplate({ id: input.id, expectedVersion: input.expectedVersion }, { signal });
        checkCurrent();
        queryClient.setQueryData<ApiPermissionTemplateCatalogResponse>(queryKeys.permissionTemplates(), current => current && ({
          ...current, templates: current.templates.filter(template => template.id !== input.id),
        }));
        continueInScope("permission-template:0");
        setSelectedId(0);
        const empty = createEmptyDraft();
        setDraft(empty);
        setPersistedDraftSnapshot(buildTemplateDraftSnapshot(empty));
        setMessage(null);
        setSuccessMessage("权限方案已删除。");
        return true;
      }
      const saved = await (input.body.id && input.body.id > 0
        ? client.updatePermissionTemplate({ id: input.body.id, body: input.body }, { signal })
        : client.createPermissionTemplate({ body: input.body }, { signal }));
      checkCurrent();
      continueInScope(`permission-template:${saved.id}`);
      queryClient.setQueryData<ApiPermissionTemplateCatalogResponse>(queryKeys.permissionTemplates(), current => current && ({
        ...current, templates: [...current.templates.filter(template => template.id !== saved.id), saved],
      }));
      const savedDraft = createDraftFromTemplate(saved, resourceByKey);
      setSelectedId(saved.id);
      setDraft(savedDraft);
      setPersistedDraftSnapshot(buildTemplateDraftSnapshot(savedDraft));
      setMessage(null);
      setSuccessMessage("权限方案草稿已保存。发布并明确应用后，才改变已绑定身份的权限。");
      return true;
    },
    refreshKeys: changed => changed ? [...identityQueryRoots, ["office"], ["worklist"]] : [],
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const saveMutation = { isPending: command.isPending, mutate: (body: ApiPermissionTemplateSaveRequest) => command.mutate({ kind: "save", body }) };
  const deleteMutation = { isPending: command.isPending, mutate: (target: { id: number; expectedVersion: number }) => command.mutate({ kind: "delete", ...target }) };

  const currentDraftSnapshot = useMemo(() => buildTemplateDraftSnapshot(draft), [draft]);
  const hasUnsavedTemplateChanges = Boolean(
    canManageUsers && selectedId != null &&
    (persistedDraftSnapshot == null || currentDraftSnapshot !== persistedDraftSnapshot),
  );
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedTemplateChanges,
    message: "当前权限方案有未保存的修改。",
  });

  const navigationScope = useMemo(() => ({}), [client, canManageUsers, selectedId, currentDraftSnapshot]);
  const navigation = useAbortableOperationController(navigationScope, { cancelPrevious: true });
  const navigate = async (action: (signal: AbortSignal) => Promise<void>) => {
    if (!canManageUsers || command.isPending) return;
    try {
      await navigation.run(action);
    } catch (error) {
      if (!isAbortError(error)) setMessage(readApiError(error));
    }
  };
  const isAdminTemplate = draft.isSystem && draft.code.toLowerCase() === "admin";
  const isBusy = publicationBusy || catalogQuery.isFetching || saveMutation.isPending || deleteMutation.isPending;
  const enabledActionCount = toPermissionGrants(draft.grants).filter(grant =>
    !draft.disabledModules.includes(resourceByKey.get(grant.resourceKey)?.moduleKey ?? "")).length;

  function applyTemplate(template: ApiPermissionTemplateDto) {
    const nextDraft = createDraftFromTemplate(template, resourceByKey);
    setSelectedId(template.id);
    setDraft(nextDraft);
    setPersistedDraftSnapshot(buildTemplateDraftSnapshot(nextDraft));
  }

  async function selectTemplate(template: ApiPermissionTemplateDto) {
    if (isBusy) return;
    return navigate(async signal => {
    if (template.id === selectedId || !await confirmDiscardChanges(`切换到权限方案“${template.name}”`)) return;
    signal.throwIfAborted();
    applyTemplate(template);
    setMessage(null);
    setSuccessMessage(null);
    });
  }

  async function beginNew() {
    return navigate(async signal => {
    if (!await confirmDiscardChanges("新建权限方案")) return;
    signal.throwIfAborted();
    const emptyDraft = createEmptyDraft();
    if (startingPoint === "combined") {
      const sources = templates.filter(template => sourceIds.includes(template.id) && template.isActive && template.code !== "Admin");
      if (!sources.length || sources.length !== sourceIds.length) {
        setMessage("请选择至少一个已启用的职责方案；系统管理员身份不能组合授予。");
        return;
      }
      Object.assign(emptyDraft, combinePermissionSchemes(sources, resourceByKey), {
        name: sources.map(source => source.name).join(" + ").slice(0, 120),
        description: "组合选定方案的当前配置；同一操作取较大范围，任一来源关闭的模块继续关闭。保存后独立维护。",
      });
    } else if (startingPoint !== "blank") {
      const source = templates.find(template => template.isSystem && template.code === "OfficeEmployee");
      if (!source?.isActive) {
        setMessage("请先确认普通员工方案存在且已启用，再从该方案创建。");
        return;
      }
      Object.assign(emptyDraft, createSchemeFromEmployee(source, resourceByKey, startingPoint), {
        name: schemeStartingPoints[startingPoint],
        description: "以普通员工方案为起点；按实际职责核对模块、查看与审批范围，审批步骤另行配置。",
      });
    }
    setSelectedId(0);
    setDraft(emptyDraft);
    setCreating(false);
    setPersistedDraftSnapshot(null);
    setMessage(null);
    setSuccessMessage(null);
    });
  }

  function copySelected() {
    setSelectedId(0);
    setDraft((current) => ({
      ...current,
      id: 0,
      versionNumber: 0,
      code: createCustomTemplateCode(),
      name: `${current.name || "权限方案"} 副本`,
      isSystem: false,
      isActive: true,
    }));
    setPersistedDraftSnapshot(null);
    setMessage(null);
    setSuccessMessage(null);
  }

  function saveTemplate() {
    if (!draft.code.trim() || !draft.name.trim()) {
      setMessage("方案代码和名称不能为空。");
      setSuccessMessage(null);
      return;
    }
    saveMutation.mutate({
      id: draft.id,
      code: draft.code.trim(),
      name: draft.name.trim(),
      description: draft.description.trim(),
      isActive: draft.isActive,
      grants: toPermissionGrants(draft.grants),
      disabledModules: draft.disabledModules,
      oaAssignedOnly: draft.oaAssignedOnly,
      expectedVersion: draft.id > 0 ? draft.versionNumber : 0,
    });
  }

  async function refreshTemplates() {
    return navigate(async signal => {
    if (!await confirmDiscardChanges("刷新权限方案")) return;
    signal.throwIfAborted();
    const result = await catalogQuery.refetch();
    signal.throwIfAborted();
    if (!selectedId || selectedId <= 0) return;
    const refreshed = result.data?.templates.find((template) => template.id === selectedId);
    if (refreshed && !result.isError) {
      applyTemplate(refreshed);
      setMessage(null);
      setSuccessMessage(null);
    }
    });
  }

  async function deleteSelected() {
    return navigate(async signal => {
    if (draft.id <= 0 || draft.isSystem || !await confirmDiscardChanges("删除当前权限方案")) return;
    signal.throwIfAborted();
    const persistedTemplate = templates.find((template) => template.id === draft.id);
    if (!persistedTemplate) return;
    if (!await requestConfirmation({
      title: "删除权限方案",
      description: `确定删除权限方案“${persistedTemplate.name}”吗？`,
      details: ["主账号或单位身份仍引用的方案不能删除。"],
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    signal.throwIfAborted();
    applyTemplate(persistedTemplate);
    deleteMutation.mutate({ id: persistedTemplate.id, expectedVersion: persistedTemplate.versionNumber });
    });
  }

  return {
    draft, setDraft, selectedId, selectedTemplate,
    creating, setCreating, startingPoint, setStartingPoint, sourceIds, setSourceIds,
    templates, resourceByKey, assignableResources, technicalResources, catalogQuery,
    isAdminTemplate, isBusy, enabledActionCount, message, successMessage,
    hasUnsavedTemplateChanges, saveMutation, selectTemplate, beginNew, copySelected,
    saveTemplate, refreshTemplates, deleteSelected, setPublicationBusy,
    onPublished: (versionNumber: number) => {
      const next = { ...draft, versionNumber };
      setDraft(next);
      setPersistedDraftSnapshot(buildTemplateDraftSnapshot(next));
    },
  };
}
