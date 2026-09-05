import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, Plus, RefreshCw, Save, ShieldCheck, Trash2 } from "lucide-react";
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

const scopeLabels: Record<string, string> = {
  own: "本人",
  department: "部门",
  company: "公司",
  all: "全部",
};

const presetLabels: Record<string, string> = {
  "": "自定义/不开放",
  view: "仅查看",
  operate: "日常操作",
  manage: "完整管理",
};

const presetRanks: Record<string, number> = { view: 1, operate: 2, manage: 3 };

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
  const resourceGroups = useMemo(() => {
    const groups = new Map<string, ApiPermissionResourceDefinitionDto[]>();
    for (const resource of assignableResources) {
      const group = groups.get(resource.group) ?? [];
      group.push(resource);
      groups.set(resource.group, group);
    }
    return [...groups.entries()];
  }, [assignableResources]);
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
      const savedDraft = createDraftFromTemplate(saved);
      setSelectedId(saved.id);
      setDraft(savedDraft);
      setPersistedDraftSnapshot(buildTemplateDraftSnapshot(savedDraft));
      setMessage(null);
      setSuccessMessage("权限模板已保存；使用该模板的现有会话已立即撤销，相关用户需要重新登录。");
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
    onSuccess: async (response) => {
      setSelectedId(null);
      setDraft(createEmptyDraft());
      setPersistedDraftSnapshot(null);
      setMessage(null);
      setSuccessMessage(response.message || "权限模板已删除。");
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
    message: "当前权限模板有未保存的修改。",
  });

  if (!canManageUsers) return null;
  const isAdminTemplate = draft.isSystem && draft.code.toLowerCase() === "admin";
  const isBusy = catalogQuery.isFetching || saveMutation.isPending || deleteMutation.isPending;
  const enabledActionCount = Object.keys(draft.grants).length;

  function applyTemplate(template: ApiPermissionTemplateDto) {
    const nextDraft = createDraftFromTemplate(template);
    setSelectedId(template.id);
    setDraft(nextDraft);
    setPersistedDraftSnapshot(buildTemplateDraftSnapshot(nextDraft));
    setMessage(null);
    setSuccessMessage(null);
  }

  async function selectTemplate(template: ApiPermissionTemplateDto) {
    if (template.id === selectedId || !await confirmDiscardChanges(`切换到权限模板“${template.name}”`)) return;
    applyTemplate(template);
  }

  async function beginNew() {
    if (!await confirmDiscardChanges("新建权限模板")) return;
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
      name: `${current.name || "权限模板"} 副本`,
      isSystem: false,
      isActive: true,
    }));
    setPersistedDraftSnapshot(null);
    setMessage(null);
    setSuccessMessage(null);
  }

  function saveTemplate() {
    if (!draft.code.trim() || !draft.name.trim()) {
      setMessage("模板代码和名称不能为空。");
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
    if (!await confirmDiscardChanges("刷新权限模板")) return;
    const result = await catalogQuery.refetch();
    if (!selectedId || selectedId <= 0) return;
    const refreshed = result.data?.templates.find((template) => template.id === selectedId);
    if (refreshed) applyTemplate(refreshed);
  }

  async function deleteSelected() {
    if (draft.id <= 0 || draft.isSystem || !await confirmDiscardChanges("删除当前权限模板")) return;
    const persistedTemplate = templates.find((template) => template.id === draft.id);
    if (!persistedTemplate) return;
    if (!await requestConfirmation({
      title: "删除权限模板",
      description: `确定删除权限模板“${persistedTemplate.name}”吗？`,
      details: ["正在被账号使用的模板不会被删除。"],
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
    <section className="form-section permission-template-section" aria-label="权限模板">
      <div className="section-header">
        <div><h2>岗位与权限模板</h2><p className="section-description">权限事实由业务资源、具体动作和数据范围共同组成。</p></div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新模板" aria-label="刷新模板" disabled={isBusy} onClick={() => void refreshTemplates()}><RefreshCw size={18} /></button>
          <button className="icon-button" type="button" title="新建模板" aria-label="新建模板" disabled={isBusy} onClick={() => void beginNew()}><Plus size={18} /></button>
          <button className="icon-button" type="button" title="复制当前模板" aria-label="复制当前模板" disabled={isBusy || draft.id <= 0} onClick={copySelected}><Copy size={18} /></button>
          <button className="command-button" type="button" disabled={isBusy || isAdminTemplate} onClick={saveTemplate}><Save size={17} /><span>保存模板</span></button>
          <button className="icon-button" type="button" title="删除模板" aria-label="删除模板" disabled={isBusy || draft.id <= 0 || draft.isSystem} onClick={deleteSelected}><Trash2 size={18} /></button>
        </div>
      </div>

      {catalogQuery.data?.applyPolicy ? <div className="permission-apply-policy"><ShieldCheck size={17} /><span>{catalogQuery.data.applyPolicy}</span></div> : null}
      {message ? <InlineNotice tone="error" title="权限模板操作失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}

      <div className="permission-template-layout">
        <div className="permission-template-list" role="group" aria-label="权限模板目录">
          {templates.map((template) => (
            <button key={template.id} type="button" aria-pressed={template.id === selectedId} className={template.id === selectedId ? "permission-template-card selected" : "permission-template-card"} onClick={() => void selectTemplate(template)}>
              <span><strong>{template.name}</strong>{template.isSystem ? <small>内置</small> : null}{!template.isActive ? <small>停用</small> : null}</span>
              <small>{template.description || "自定义岗位权限"}</small>
            </button>
          ))}
        </div>

        <div className="permission-template-editor">
          {isAdminTemplate ? <PermissionNotice>系统管理员模板由管理员身份固定授予全部能力，不能委托或修改。</PermissionNotice> : null}
          <div className="field-grid permission-template-meta-grid">
            <label><span>模板名称</span><input value={draft.name} disabled={isBusy || isAdminTemplate} onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))} /></label>
            <label className="permission-template-count"><span>已授权动作</span><strong>{enabledActionCount} 项</strong><small>每项均有明确数据范围</small></label>
            <label className="permission-template-description"><span>说明</span><input value={draft.description} disabled={isBusy || isAdminTemplate} onChange={(event) => setDraft((current) => ({ ...current, description: event.target.value }))} /></label>
            <label className="settings-check"><input type="checkbox" checked={draft.isActive} disabled={isBusy || draft.isSystem} onChange={(event) => setDraft((current) => ({ ...current, isActive: event.target.checked }))} /><span>启用模板</span></label>
          </div>

          <div className="permission-business-note">
            “仅查看 / 日常操作 / 完整管理”只用于快速勾选；保存的真实权限始终是下方每个动作及其数据范围。
            页面所需的查看权限齐全后，导航菜单才会自动显示；取消后菜单、直接网址、页面按钮和 API 会同步拒绝。
          </div>
          <div className="permission-resource-groups">
            {resourceGroups.map(([groupName, groupResources]) => (
              <section className="permission-resource-group" key={groupName} aria-label={groupName}>
                <h3>{groupName}</h3>
                {groupResources.map((resource) => (
                  <div className="permission-resource-card" key={resource.key}>
                    <div className="permission-resource-header">
                      <strong>{resource.name}</strong>
                      <label><span>快捷预设</span><select disabled={isBusy || isAdminTemplate} value={detectPreset(draft.grants, resource)} onChange={(event) => applyPreset(resource, event.target.value)}>{Object.entries(presetLabels).map(([value, label]) => <option key={value || "custom"} value={value}>{label}</option>)}</select></label>
                    </div>
                    <div className="permission-action-list">
                      {resource.actions.map((action) => {
                        const key = grantKey(resource.key, action.key);
                        const enabled = Object.prototype.hasOwnProperty.call(draft.grants, key);
                        return (
                          <div className="permission-action-row" key={action.key}>
                            <label className="permission-action-toggle"><input type="checkbox" checked={enabled} disabled={isBusy || isAdminTemplate} onChange={(event) => toggleAction(resource, action.key, event.target.checked)} /><span><strong>{action.name}</strong><small>{action.description}</small></span></label>
                            {resource.supportsDataScope ? <select aria-label={`${resource.name}${action.name}数据范围`} value={draft.grants[key] ?? "own"} disabled={!enabled || isBusy || isAdminTemplate} onChange={(event) => patchScope(resource.key, action.key, event.target.value)}>{(catalogQuery.data?.dataScopes ?? []).map((scope) => <option key={scope} value={scope}>{scopeLabels[scope] ?? scope}</option>)}</select> : <span className="permission-global-scope">全局</span>}
                          </div>
                        );
                      })}
                    </div>
                  </div>
                ))}
              </section>
            ))}
          </div>

          <EffectivePermissionSummary effectiveGrants={selectedTemplate?.effectiveGrants ?? []} resourceByKey={resourceByKey} />
          <TechnicalPermissionCatalog
            resources={technicalResources}
            effectiveGrants={selectedTemplate?.effectiveGrants ?? []}
          />

          <details className="permission-advanced-details">
            <summary>高级信息</summary>
            <label><span>模板代码</span><input value={draft.code} disabled={isBusy || draft.isSystem} onChange={(event) => setDraft((current) => ({ ...current, code: event.target.value }))} /><small>用于系统内部识别；一般无需修改。</small></label>
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
      <p>这些模块用于基础资料读取、输出链路和系统维护。岗位模板不能直接授予；业务动作会按最小范围自动继承，系统维护仍要求管理员身份。</p>
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
    <details className="permission-effective-details" open>
      <summary>最终有效权限与技术依赖（只读）</summary>
      <p>这里显示最近一次保存后由服务端计算的继承结果；修改草稿后保存即可刷新。</p>
      {inherited.length === 0 ? <span className="empty-inline">没有额外技术依赖。</span> : <ul>{inherited.map((grant) => {
        const resource = resourceByKey.get(grant.resourceKey);
        const action = resource?.actions.find((item) => item.key === grant.action);
        const source = grant.sourceResourceKey ? resourceByKey.get(grant.sourceResourceKey)?.name || grant.sourceResourceKey : "岗位模板";
        return <li key={`${grant.resourceKey}:${grant.action}`}><strong>{resource?.name || grant.resourceKey} · {action?.name || grant.action}</strong><span>{scopeLabels[grant.dataScope] ?? grant.dataScope}；来源：{grant.source === "dependency" ? `继承自 ${source}` : source}</span></li>;
      })}</ul>}
    </details>
  );
}

export default PermissionTemplateManagementPanel;

function createDraftFromTemplate(template: ApiPermissionTemplateDto): TemplateDraft {
  return {
    id: template.id,
    versionNumber: template.versionNumber ?? 1,
    code: template.code,
    name: template.name,
    description: template.description ?? "",
    isSystem: template.isSystem,
    isActive: template.isActive,
    grants: Object.fromEntries(template.grants.map((grant) => [grantKey(grant.resourceKey, grant.action), grant.dataScope])),
  };
}

function createEmptyDraft(): TemplateDraft {
  return { id: 0, versionNumber: 0, code: createCustomTemplateCode(), name: "新权限模板", description: "", isSystem: false, isActive: true, grants: {} };
}

function buildTemplateDraftSnapshot(draft: TemplateDraft) {
  return JSON.stringify({ ...draft, grants: Object.fromEntries(Object.entries(draft.grants).sort(([left], [right]) => left.localeCompare(right))) });
}

function grantKey(resourceKey: string, action: string) {
  return `${resourceKey}\u001f${action}`;
}

function splitGrantKey(key: string) {
  const [resourceKey = "", action = ""] = key.split("\u001f", 2);
  return [resourceKey, action] as const;
}

function firstResourceScope(grants: Record<string, string>, resourceKey: string) {
  return Object.entries(grants).find(([key]) => splitGrantKey(key)[0] === resourceKey)?.[1] ?? "";
}

function detectPreset(grants: Record<string, string>, resource: ApiPermissionResourceDefinitionDto) {
  const enabled = resource.actions.filter((action) => Object.prototype.hasOwnProperty.call(grants, grantKey(resource.key, action.key)));
  if (enabled.length === 0) return "";
  for (const level of ["view", "operate", "manage"]) {
    const expected = resource.actions.filter((action) => (presetRanks[action.presetLevel] ?? 0) <= presetRanks[level]);
    if (expected.length === enabled.length && expected.every((action) => enabled.includes(action))) return level;
  }
  return "";
}

function createCustomTemplateCode() {
  const now = new Date();
  const compact = [now.getFullYear(), String(now.getMonth() + 1).padStart(2, "0"), String(now.getDate()).padStart(2, "0"), String(now.getHours()).padStart(2, "0"), String(now.getMinutes()).padStart(2, "0")].join("");
  return `custom-${compact}`;
}
