import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, Plus, RefreshCw, Save, Trash2 } from "lucide-react";
import type {
  ApiEffectivePermissionGrantDto,
  ApiPermissionResourceDefinitionDto,
  ApiPermissionTemplateDto,
  ApiPermissionTemplateSaveRequest,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { PermissionModuleGrid } from "./PermissionModuleGrid.tsx";
import { firstResourceScope, getEditableSchemeGrants, grantKey, presetRanks, scopeLabels, splitGrantKey } from "./permissionSchemeModel.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";

type TemplateDraft = {
  id: number;
  versionNumber: number;
  code: string;
  name: string;
  description: string;
  isSystem: boolean;
  isActive: boolean;
  grants: Record<string, string>;
};

export function PermissionTemplateManagementPanel({
  client,
  canManageUsers,
}: {
  client: ExportDocManagerApiClient;
  canManageUsers: boolean;
}) {
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<TemplateDraft>(() => createEmptyDraft());
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [persistedDraftSnapshot, setPersistedDraftSnapshot] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

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

  const saveMutation = useMutation({
    mutationFn: (body: ApiPermissionTemplateSaveRequest) => draft.id > 0
      ? client.updatePermissionTemplate({ id: draft.id, body })
      : client.createPermissionTemplate({ body }),
    onSuccess: async (saved) => {
      const savedDraft = createDraftFromTemplate(saved, resourceByKey);
      setSelectedId(saved.id);
      setDraft(savedDraft);
      setPersistedDraftSnapshot(buildTemplateDraftSnapshot(savedDraft));
      setMessage(null);
      setSuccessMessage("权限方案已保存；使用该方案的现有会话已立即撤销，相关用户需要重新登录。");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.permissionTemplates() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.users() }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (target: { id: number; expectedVersion: number }) => client.deletePermissionTemplate(target),
    onSuccess: async () => {
      setSelectedId(null);
      setDraft(createEmptyDraft());
      setPersistedDraftSnapshot(null);
      setMessage(null);
      setSuccessMessage("权限方案已删除。");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.permissionTemplates() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.users() }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const currentDraftSnapshot = useMemo(() => buildTemplateDraftSnapshot(draft), [draft]);
  const hasUnsavedTemplateChanges = Boolean(
    canManageUsers && selectedId != null &&
    (persistedDraftSnapshot == null || currentDraftSnapshot !== persistedDraftSnapshot),
  );
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedTemplateChanges,
    message: "当前权限方案有未保存的修改。",
  });

  if (!canManageUsers) return null;
  const isAdminTemplate = draft.isSystem && draft.code.toLowerCase() === "admin";
  const isBusy = catalogQuery.isFetching || saveMutation.isPending || deleteMutation.isPending;
  const enabledActionCount = Object.keys(draft.grants).length;

  function applyTemplate(template: ApiPermissionTemplateDto) {
    const nextDraft = createDraftFromTemplate(template, resourceByKey);
    setSelectedId(template.id);
    setDraft(nextDraft);
    setPersistedDraftSnapshot(buildTemplateDraftSnapshot(nextDraft));
  }

  async function selectTemplate(template: ApiPermissionTemplateDto) {
    if (template.id === selectedId || !await confirmDiscardChanges(`切换到权限方案“${template.name}”`)) return;
    applyTemplate(template);
    setMessage(null);
    setSuccessMessage(null);
  }

  async function beginNew() {
    if (!await confirmDiscardChanges("新建权限方案")) return;
    const emptyDraft = createEmptyDraft();
    setSelectedId(0);
    setDraft(emptyDraft);
    setPersistedDraftSnapshot(buildTemplateDraftSnapshot(emptyDraft));
    setMessage(null);
    setSuccessMessage(null);
  }

  function copySelected() {
    const suffix = new Date().toISOString().replace(/[-:TZ.]/g, "").slice(0, 12);
    setSelectedId(0);
    setDraft((current) => ({
      ...current,
      id: 0,
      versionNumber: 0,
      code: `custom-${suffix}`,
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
      grants: Object.entries(draft.grants).map(([key, dataScope]) => {
        const [resourceKey, action] = splitGrantKey(key);
        return { resourceKey, action, dataScope };
      }),
      expectedVersion: draft.id > 0 ? draft.versionNumber : 0,
    });
  }

  async function refreshTemplates() {
    if (!await confirmDiscardChanges("刷新权限方案")) return;
    const result = await catalogQuery.refetch();
    if (!selectedId || selectedId <= 0) return;
    const refreshed = result.data?.templates.find((template) => template.id === selectedId);
    if (refreshed && !result.isError) {
      applyTemplate(refreshed);
      setMessage(null);
      setSuccessMessage(null);
    }
  }

  async function deleteSelected() {
    if (draft.id <= 0 || draft.isSystem || !await confirmDiscardChanges("删除当前权限方案")) return;
    const persistedTemplate = templates.find((template) => template.id === draft.id);
    if (!persistedTemplate) return;
    if (!await requestConfirmation({
      title: "删除权限方案",
      description: `确定删除权限方案“${persistedTemplate.name}”吗？`,
      details: ["正在被账号使用的方案不会被删除。"],
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    applyTemplate(persistedTemplate);
    deleteMutation.mutate({ id: persistedTemplate.id, expectedVersion: persistedTemplate.versionNumber });
  }

  function toggleAction(resource: ApiPermissionResourceDefinitionDto, action: string, enabled: boolean) {
    const key = grantKey(resource.key, action);
    setDraft((current) => {
      const grants = { ...current.grants };
      if (enabled) grants[key] = resource.supportsDataScope ? grants[key] || "own" : "all";
      else delete grants[key];
      return { ...current, grants };
    });
    setSuccessMessage(null);
  }

  function patchScope(resourceKey: string, action: string, dataScope: string) {
    setDraft((current) => ({ ...current, grants: { ...current.grants, [grantKey(resourceKey, action)]: dataScope } }));
    setSuccessMessage(null);
  }

  function applyPreset(resource: ApiPermissionResourceDefinitionDto, level: string) {
    setDraft((current) => {
      const grants = Object.fromEntries(Object.entries(current.grants).filter(([key]) => splitGrantKey(key)[0] !== resource.key));
      if (level) {
        const defaultScope = firstResourceScope(current.grants, resource.key) || (resource.supportsDataScope ? "own" : "all");
        for (const action of resource.actions) {
          if ((presetRanks[action.presetLevel] ?? 0) <= (presetRanks[level] ?? 0)) {
            grants[grantKey(resource.key, action.key)] = defaultScope;
          }
        }
      }
      return { ...current, grants };
    });
    setSuccessMessage(null);
  }

  return (
    <section className="form-section permission-template-section" aria-label="权限方案">
      <div className="section-header">
        <div><h2>权限方案</h2><p className="section-description">选择岗位方案，设置各功能模块的操作权限。</p></div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新方案" aria-label="刷新方案" disabled={isBusy} onClick={() => void refreshTemplates()}><RefreshCw size={18} /></button>
          <button className="icon-button" type="button" title="新建方案" aria-label="新建方案" disabled={isBusy} onClick={() => void beginNew()}><Plus size={18} /></button>
          <button className="icon-button" type="button" title="复制当前方案" aria-label="复制当前方案" disabled={isBusy || draft.id <= 0} onClick={copySelected}><Copy size={18} /></button>
          <button className="command-button" type="button" disabled={isBusy || isAdminTemplate} onClick={saveTemplate}><Save size={17} /><span>保存方案</span></button>
          <button className="icon-button" type="button" title="删除方案" aria-label="删除方案" disabled={isBusy || draft.id <= 0 || draft.isSystem} onClick={deleteSelected}><Trash2 size={18} /></button>
        </div>
      </div>

      {message ? <InlineNotice tone="error" title="权限方案操作失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}

      <div className="permission-template-layout">
        <div className="permission-template-list" role="group" aria-label="权限方案目录">
          {templates.map((template) => (
            <button key={template.id} type="button" title={template.description || template.name} aria-pressed={template.id === selectedId} className={template.id === selectedId ? "permission-template-card selected" : "permission-template-card"} onClick={() => void selectTemplate(template)}>
              <span><strong>{template.name}</strong>{template.isSystem ? <small>内置</small> : null}{!template.isActive ? <small>停用</small> : null}</span>
            </button>
          ))}
        </div>

        <div className="permission-template-editor">
          {isAdminTemplate ? <PermissionNotice>系统管理员方案由管理员身份固定授予全部能力，不能委托或修改。</PermissionNotice> : null}
          <div className="field-grid permission-template-meta-grid">
            <label><span>方案名称</span><input value={draft.name} disabled={isBusy || isAdminTemplate} onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))} /></label>
            <label><span>说明</span><input placeholder="适用岗位或使用说明（可选）" value={draft.description} disabled={isBusy || isAdminTemplate} onChange={(event) => setDraft((current) => ({ ...current, description: event.target.value }))} /></label>
          </div>
          <div className="permission-template-status">
            <label className="checkbox-field"><input type="checkbox" checked={draft.isActive} disabled={isBusy || draft.isSystem} onChange={(event) => setDraft((current) => ({ ...current, isActive: event.target.checked }))} /><span>启用方案</span></label>
            <span>已开通 <strong>{enabledActionCount}</strong> 项操作</span>
          </div>
          <PermissionModuleGrid
            resources={assignableResources}
            grants={draft.grants}
            dataScopes={catalogQuery.data?.dataScopes ?? []}
            disabled={isBusy || isAdminTemplate}
            onToggle={toggleAction}
            onScopeChange={patchScope}
            onPresetChange={applyPreset}
          />
          <details className="permission-effective-details">
            <summary>权限设置说明</summary>
            <p>权限方案是一组岗位授权；功能模块是发票、客户等业务功能。勾选操作后可设置本人、部门、公司或全部数据范围，也可用快捷设置批量勾选。拥有模块所需的查看权限后，导航菜单自动显示。</p>
          </details>
          <EffectivePermissionSummary effectiveGrants={selectedTemplate?.effectiveGrants ?? []} resourceByKey={resourceByKey} />
          <TechnicalPermissionCatalog
            resources={technicalResources}
            effectiveGrants={selectedTemplate?.effectiveGrants ?? []}
          />

          <details className="permission-advanced-details">
            <summary>高级信息</summary>
            <label><span>方案代码</span><input value={draft.code} disabled={isBusy || draft.isSystem} onChange={(event) => setDraft((current) => ({ ...current, code: event.target.value }))} /><small>用于系统内部识别；一般无需修改。</small></label>
          </details>
        </div>
      </div>
    </section>
  );
}

function TechnicalPermissionCatalog({
  resources,
  effectiveGrants,
}: {
  resources: ApiPermissionResourceDefinitionDto[];
  effectiveGrants: ApiEffectivePermissionGrantDto[];
}) {
  const effectiveKeys = new Set(effectiveGrants.map((grant) => grantKey(grant.resourceKey, grant.action)));
  return (
    <details className="permission-effective-details permission-technical-details">
      <summary>身份限定与技术依赖模块（只读，共 {resources.length} 个）</summary>
      <p>这些模块用于基础资料读取、输出链路和系统维护。岗位方案不能直接授予；业务动作会按最小范围自动继承，系统维护仍要求管理员身份。</p>
      <div className="permission-technical-grid">
        {resources.map((resource) => {
          const activeActions = resource.actions.filter((action) =>
            effectiveKeys.has(grantKey(resource.key, action.key)));
          return (
            <section key={resource.key} className="permission-technical-card">
              <div><strong>{resource.name}</strong><small>{resource.group}</small></div>
              <span>{activeActions.length > 0 ? `当前生效 ${activeActions.length} 项` : "当前未生效"}</span>
              <ul>{resource.actions.map((action) => (
                <li key={action.key} className={effectiveKeys.has(grantKey(resource.key, action.key)) ? "active" : ""}>
                  {action.name}
                </li>
              ))}</ul>
            </section>
          );
        })}
      </div>
    </details>
  );
}

function EffectivePermissionSummary({
  effectiveGrants,
  resourceByKey,
}: {
  effectiveGrants: ApiEffectivePermissionGrantDto[];
  resourceByKey: ReadonlyMap<string, ApiPermissionResourceDefinitionDto>;
}) {
  const inherited = effectiveGrants.filter((grant) => grant.source !== "template" || resourceByKey.get(grant.resourceKey)?.isTechnical);
  return (
    <details className="permission-effective-details">
      <summary>最终有效权限与技术依赖（只读）</summary>
      <p>这里显示最近一次保存后由服务端计算的继承结果；修改草稿后保存即可刷新。</p>
      {inherited.length === 0 ? <span className="empty-inline">没有额外技术依赖。</span> : <ul>{inherited.map((grant) => {
        const resource = resourceByKey.get(grant.resourceKey);
        const action = resource?.actions.find((item) => item.key === grant.action);
        const source = grant.sourceResourceKey ? resourceByKey.get(grant.sourceResourceKey)?.name || grant.sourceResourceKey : "岗位方案";
        return <li key={`${grant.resourceKey}:${grant.action}`}><strong>{resource?.name || grant.resourceKey} · {action?.name || grant.action}</strong><span>{scopeLabels[grant.dataScope] ?? grant.dataScope}；来源：{grant.source === "dependency" ? `继承自 ${source}` : source}</span></li>;
      })}</ul>}
    </details>
  );
}

export default PermissionTemplateManagementPanel;

function createDraftFromTemplate(template: ApiPermissionTemplateDto, resources: ReadonlyMap<string, ApiPermissionResourceDefinitionDto>): TemplateDraft {
  return {
    id: template.id,
    versionNumber: template.versionNumber ?? 1,
    code: template.code,
    name: template.name,
    description: template.description ?? "",
    isSystem: template.isSystem,
    isActive: template.isActive,
    grants: getEditableSchemeGrants(template.grants, resources),
  };
}

function createEmptyDraft(): TemplateDraft {
  return { id: 0, versionNumber: 0, code: createCustomTemplateCode(), name: "新权限方案", description: "", isSystem: false, isActive: true, grants: {} };
}

function buildTemplateDraftSnapshot(draft: TemplateDraft) {
  return JSON.stringify({ ...draft, grants: Object.fromEntries(Object.entries(draft.grants).sort(([left], [right]) => left.localeCompare(right))) });
}

function createCustomTemplateCode() {
  const now = new Date();
  const compact = [now.getFullYear(), String(now.getMonth() + 1).padStart(2, "0"), String(now.getDate()).padStart(2, "0"), String(now.getHours()).padStart(2, "0"), String(now.getMinutes()).padStart(2, "0")].join("");
  return `custom-${compact}`;
}
