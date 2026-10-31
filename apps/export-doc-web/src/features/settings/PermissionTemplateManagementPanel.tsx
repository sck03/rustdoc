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
import { identityQueryRoots, queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { PermissionModuleGrid } from "./PermissionModuleGrid.tsx";
import { combinePermissionSchemes, createSchemeFromEmployee, getEditableSchemeGrants, grantKey, schemeStartingPoints, schemeActionDataScopes, scopeLabels, toPermissionGrants, type SchemeStartingPoint } from "./permissionSchemeModel.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { OfficeDialog } from "../office/OfficeUi.tsx";
import { PermissionPublicationActions } from "./PermissionPublicationActions.tsx";

type TemplateDraft = {
  id: number;
  versionNumber: number;
  code: string;
  name: string;
  description: string;
  isSystem: boolean;
  isActive: boolean;
  grants: Record<string, string>;
  disabledModules: string[];
  oaAssignedOnly: boolean;
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
  const [startingPoint, setStartingPoint] = useState<SchemeStartingPoint>("blank");
  const [sourceIds, setSourceIds] = useState<number[]>([]);
  const [creating, setCreating] = useState(false);
  const [publicationBusy,setPublicationBusy]=useState(false);

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
      setSuccessMessage("权限方案草稿已保存。发布并明确应用后，才改变已绑定身份的权限。");
      await Promise.all([...identityQueryRoots, ["office"], ["worklist"]].map(queryKey => queryClient.invalidateQueries({ queryKey })));
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
      await Promise.all([...identityQueryRoots, ["office"], ["worklist"]].map(queryKey => queryClient.invalidateQueries({ queryKey })));
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
    if(isBusy)return;
    if (template.id === selectedId || !await confirmDiscardChanges(`切换到权限方案“${template.name}”`)) return;
    applyTemplate(template);
    setMessage(null);
    setSuccessMessage(null);
  }

  async function beginNew() {
    if (!await confirmDiscardChanges("新建权限方案")) return;
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
      details: ["主账号或单位身份仍引用的方案不能删除。"],
      confirmLabel: "确认删除",
      tone: "danger",
    })) return;
    applyTemplate(persistedTemplate);
    deleteMutation.mutate({ id: persistedTemplate.id, expectedVersion: persistedTemplate.versionNumber });
  }

  return (
    <section className="form-section permission-template-section" aria-label="权限方案">
      <div className="section-header">
        <div><h2>权限方案</h2><p className="section-description">主账号与单位身份明确选择共享方案，也可独立配置。方案关闭模块同时限制菜单和接口；已保存方案不会随岗位名称或程序升级自动增权。</p></div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新方案" aria-label="刷新方案" disabled={isBusy} onClick={() => void refreshTemplates()}><RefreshCw size={18} /></button>
          <button className="icon-button" type="button" title="新建方案" aria-label="新建方案" disabled={isBusy} onClick={() => setCreating(true)}><Plus size={18} /></button>
          <button className="icon-button" type="button" title="复制当前方案" aria-label="复制当前方案" disabled={isBusy || draft.id <= 0} onClick={copySelected}><Copy size={18} /></button>
          <button className="command-button" type="button" disabled={isBusy || isAdminTemplate} onClick={saveTemplate}><Save size={17} /><span>保存方案</span></button>
          <button className="icon-button" type="button" title="删除方案" aria-label="删除方案" disabled={isBusy || draft.id <= 0 || draft.isSystem} onClick={deleteSelected}><Trash2 size={18} /></button>
        </div>
      </div>

      {message ? <InlineNotice tone="error" title="权限方案操作失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}

      {creating && <OfficeDialog title="新建权限方案" onClose={() => setCreating(false)}>
        <label className="office-field"><span>新建方案起点</span><select aria-label="新建方案起点" value={startingPoint} disabled={isBusy} onChange={event => setStartingPoint(event.target.value as SchemeStartingPoint)}>
          {Object.entries(schemeStartingPoints).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select></label>
        {startingPoint === "combined" && <fieldset className="permission-scheme-sources" disabled={isBusy}><legend>选择需要兼任的职责</legend>
          {templates.filter(template => template.isActive && template.code !== "Admin").map(template => <label key={template.id} className="checkbox-field">
            <input type="checkbox" checked={sourceIds.includes(template.id)} onChange={event => setSourceIds(current => event.target.checked ? [...current, template.id] : current.filter(id => id !== template.id))} />{template.name}
          </label>)}
          <p>组合当前配置，同一操作取较大范围，关闭模块保留；新方案独立维护。</p>
        </fieldset>}
        {message && <InlineNotice tone="error">{message}</InlineNotice>}
        <button className="command-button" type="button" disabled={isBusy} onClick={() => void beginNew()}>建立方案草稿</button>
      </OfficeDialog>}

      <div className="permission-template-layout">
        <div className="permission-template-list" role="group" aria-label="权限方案目录">
          {templates.map((template) => (
            <button key={template.id} type="button" title={[template.name, template.description].filter(Boolean).join("：")} aria-pressed={template.id === selectedId} className={template.id === selectedId ? "permission-template-card selected" : "permission-template-card"} onClick={() => void selectTemplate(template)}>
              <strong>{template.name}</strong>
              {(template.isSystem || !template.isActive || template.ownerUnitId != null) && <span className="permission-template-badges">{template.isSystem && <small>内置</small>}{!template.isActive && <small>停用</small>}{template.ownerUnitId != null && <small>单位专用</small>}</span>}
              <small className="permission-template-references">{template.accountReferenceCount ?? 0} 个主账号 · {template.unitRoleReferenceCount ?? 0} 个单位身份</small>
            </button>
          ))}
        </div>

        <div className="permission-template-editor">
          {selectedTemplate&&!isAdminTemplate&&<PermissionPublicationActions key={selectedTemplate.id} client={client} scheme={selectedTemplate} disabled={hasUnsavedTemplateChanges||saveMutation.isPending} accountTargets onBusy={setPublicationBusy} onPublished={versionNumber=>{const next={...draft,versionNumber};setDraft(next);setPersistedDraftSnapshot(buildTemplateDraftSnapshot(next));}} />}
          {isAdminTemplate ? <PermissionNotice>系统管理员方案由管理员身份固定授予全部能力，不能委托或修改。</PermissionNotice> : null}
          <div className="field-grid permission-template-meta-grid">
            <label><span>方案名称</span><input value={draft.name} disabled={isBusy || isAdminTemplate} onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))} /></label>
            <label><span>说明</span><input placeholder="适用岗位或使用说明（可选）" value={draft.description} disabled={isBusy || isAdminTemplate} onChange={(event) => setDraft((current) => ({ ...current, description: event.target.value }))} /></label>
          </div>
          <div className="permission-template-status">
            <label className="checkbox-field"><input type="checkbox" checked={draft.isActive} disabled={isBusy || draft.isSystem} onChange={(event) => setDraft((current) => ({ ...current, isActive: event.target.checked }))} /><span>启用方案</span></label>
            <span>已开通 <strong>{enabledActionCount}</strong> 项操作</span>
            <label className="checkbox-field"><input type="checkbox" checked={draft.oaAssignedOnly} disabled={isBusy || isAdminTemplate} onChange={event => setDraft(current => ({ ...current, oaAssignedOnly: event.target.checked }))} />仅查看本人及分派给自己的申请</label>
          </div>
          <PermissionModuleGrid
            resources={assignableResources}
            grants={draft.grants}
            dataScopes={catalogQuery.data?.dataScopes ?? []}
            actionDataScopes={schemeActionDataScopes(assignableResources,catalogQuery.data?.dataScopes ?? [])}
            disabled={isBusy || isAdminTemplate}
            onChange={grants => setDraft(current => ({ ...current, grants }))}
            disabledModules={draft.disabledModules}
            onModulesChange={disabledModules => setDraft(current => ({ ...current, disabledModules }))}
          />
          <details className="permission-effective-details">
            <summary>权限设置说明</summary>
            <p>总经理等跨部门负责人可从“公司审批负责人”新建方案，部门主管可从“部门审批负责人”开始，再按职责增减。方案复制普通员工的操作和关闭模块，只增加人事、行政的查看与审批；查看范围内的草稿与历史也可见。审批步骤、财务接收、档案维护及系统管理分别配置。部门范围包含主职、兼职及本人负责的部门与启用子部门；可另启用仅本人和分派申请可见。</p>
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
  const inherited = effectiveGrants.filter((grant) => grant.source === "dependency" || resourceByKey.get(grant.resourceKey)?.isTechnical);
  return (
    <details className="permission-effective-details">
      <summary>最终有效权限与技术依赖（只读）</summary>
      <p>这里显示最近一次保存后由服务端计算的继承结果；修改草稿后保存即可刷新。</p>
      {inherited.length === 0 ? <span className="empty-inline">没有额外技术依赖。</span> : <ul>{inherited.map((grant) => {
        const resource = resourceByKey.get(grant.resourceKey);
        const action = resource?.actions.find((item) => item.key === grant.action);
        const source = grant.sourceResourceKey ? resourceByKey.get(grant.sourceResourceKey)?.name || grant.sourceResourceKey : "岗位方案";
        return <li key={`${grant.resourceKey}:${grant.action}`}><strong>{resource?.name || grant.resourceKey} · {action?.name || grant.action}</strong><span>{resource?.supportsDataScope ? scopeLabels[grant.dataScope] ?? grant.dataScope : resource?.scopeDescription ?? "功能授权"}；来源：{grant.source === "dependency" ? `继承自 ${source}` : source}</span></li>;
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
    disabledModules: template.disabledModules ?? [],
    oaAssignedOnly: template.oaAssignedOnly ?? false,
  };
}

function createEmptyDraft(): TemplateDraft {
  return { id: 0, versionNumber: 0, code: createCustomTemplateCode(), name: "新权限方案", description: "", isSystem: false, isActive: true, grants: {}, disabledModules: [], oaAssignedOnly: false };
}

function buildTemplateDraftSnapshot(draft: TemplateDraft) {
  return JSON.stringify({ ...draft, grants: Object.fromEntries(Object.entries(draft.grants).sort(([left], [right]) => left.localeCompare(right))) });
}

function createCustomTemplateCode() {
  return `custom-${createRequestKey()}`;
}
