import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, Plus, RefreshCw, Save, ShieldCheck, Trash2 } from "lucide-react";
import type {
  ApiPermissionModuleDefinitionDto,
  ApiPermissionTemplateDto,
  ApiPermissionTemplateSaveRequest,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";

type TemplateDraft = {
  id: number;
  code: string;
  name: string;
  description: string;
  isSystem: boolean;
  isActive: boolean;
  moduleAccess: Record<string, string>;
};

const accessLevelLabels: Record<string, string> = {
  "": "不开放",
  view: "仅查看",
  operate: "可操作",
  manage: "可管理",
};

const accessLevelDescriptions = [
  { level: "view", label: "仅查看", description: "可以浏览列表、详情和查询结果" },
  { level: "operate", label: "可操作", description: "可以新增、修改和执行日常业务" },
  { level: "manage", label: "可管理", description: "包含删除、模板维护等管理操作" },
];

export function PermissionTemplateManagementPanel({
  client,
  canManageUsers,
}: {
  client: ExportDocManagerApiClient;
  canManageUsers: boolean;
}) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<TemplateDraft>(() => createEmptyDraft());
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const catalogQuery = useQuery({
    queryKey: queryKeys.permissionTemplates(),
    queryFn: () => client.listPermissionTemplates(),
    enabled: canManageUsers,
  });
  const templates = catalogQuery.data?.templates ?? [];
  const businessModules = useMemo(
    () => (catalogQuery.data?.modules ?? []).filter((module) => !module.isTechnical),
    [catalogQuery.data],
  );
  const businessModuleKeys = useMemo(() => new Set(businessModules.map((module) => module.key)), [businessModules]);
  const moduleGroups = useMemo(() => {
    const groups = new Map<string, ApiPermissionModuleDefinitionDto[]>();
    for (const module of businessModules) {
      const group = groups.get(module.group) ?? [];
      group.push(module);
      groups.set(module.group, group);
    }
    return [...groups.entries()];
  }, [businessModules]);

  useEffect(() => {
    if (selectedId == null && templates.length > 0) {
      selectTemplate(templates[0]);
    }
  }, [selectedId, templates]);

  useEffect(() => {
    if (catalogQuery.isError) {
      setMessage(readApiError(catalogQuery.error));
      setSuccessMessage(null);
    }
  }, [catalogQuery.error, catalogQuery.isError]);

  const saveMutation = useMutation({
    mutationFn: (body: ApiPermissionTemplateSaveRequest) =>
      draft.id > 0
        ? client.updatePermissionTemplate({ id: draft.id, body })
        : client.createPermissionTemplate({ body }),
    onSuccess: async (saved) => {
      setSelectedId(saved.id);
      setDraft(createDraftFromTemplate(saved));
      setMessage(null);
      setSuccessMessage("权限模板已保存；已登录用户重新登录后生效。");
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
    mutationFn: (id: number) => client.deletePermissionTemplate({ id }),
    onSuccess: async (response) => {
      setSelectedId(null);
      setDraft(createEmptyDraft());
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

  if (!canManageUsers) return null;
  const isAdminTemplate = draft.isSystem && draft.code.toLowerCase() === "admin";
  const isBusy = catalogQuery.isFetching || saveMutation.isPending || deleteMutation.isPending;
  const enabledBusinessModuleCount = Object.entries(draft.moduleAccess)
    .filter(([moduleKey, accessLevel]) => businessModuleKeys.has(moduleKey) && Boolean(accessLevel)).length;

  function selectTemplate(template: ApiPermissionTemplateDto) {
    setSelectedId(template.id);
    setDraft(createDraftFromTemplate(template));
    setMessage(null);
    setSuccessMessage(null);
  }

  function beginNew() {
    setSelectedId(0);
    setDraft(createEmptyDraft());
    setMessage(null);
    setSuccessMessage(null);
  }

  function copySelected() {
    const suffix = new Date().toISOString().replace(/[-:TZ.]/g, "").slice(0, 12);
    setSelectedId(0);
    setDraft((current) => ({
      ...current,
      id: 0,
      code: `custom-${suffix}`,
      name: `${current.name || "权限模板"} 副本`,
      isSystem: false,
      isActive: true,
    }));
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
      id: draft.id || undefined,
      code: draft.code.trim(),
      name: draft.name.trim(),
      description: draft.description.trim(),
      isActive: draft.isActive,
      modules: Object.entries(draft.moduleAccess)
        .filter(([moduleKey, accessLevel]) => businessModuleKeys.has(moduleKey) && Boolean(accessLevel))
        .map(([moduleKey, accessLevel]) => ({ moduleKey, accessLevel })),
    });
  }

  function deleteSelected() {
    if (draft.id <= 0 || draft.isSystem) return;
    if (!window.confirm(`确定删除权限模板“${draft.name}”吗？正在被账号使用的模板不会被删除。`)) return;
    deleteMutation.mutate(draft.id);
  }

  function patchAccess(moduleKey: string, accessLevel: string) {
    setDraft((current) => ({
      ...current,
      moduleAccess: { ...current.moduleAccess, [moduleKey]: accessLevel },
    }));
    setSuccessMessage(null);
  }

  return (
    <section className="form-section permission-template-section" aria-label="权限模板">
      <div className="section-header">
        <div>
          <h2>权限模板</h2>
          <p className="section-description">按岗位选择业务模块即可；界面导航和服务端接口会执行同一套权限规则。</p>
        </div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新模板" disabled={isBusy} onClick={() => void catalogQuery.refetch()}><RefreshCw size={18} /></button>
          <button className="icon-button" type="button" title="新建模板" disabled={isBusy} onClick={beginNew}><Plus size={18} /></button>
          <button className="icon-button" type="button" title="复制当前模板" disabled={isBusy || draft.id <= 0} onClick={copySelected}><Copy size={18} /></button>
          <button className="command-button" type="button" disabled={isBusy || isAdminTemplate} onClick={saveTemplate}><Save size={17} /><span>保存模板</span></button>
          <button className="icon-button" type="button" title="删除模板" disabled={isBusy || draft.id <= 0 || draft.isSystem} onClick={deleteSelected}><Trash2 size={18} /></button>
        </div>
      </div>

      {catalogQuery.data?.applyPolicy ? <div className="permission-apply-policy"><ShieldCheck size={17} /><span>{catalogQuery.data.applyPolicy}</span></div> : null}
      <div className="permission-business-note">
        这里只显示业务模块。基础资料读取、候选项和单据输出等技术权限会由系统自动补齐，无需管理员理解或逐项配置。
      </div>
      {message ? <div className="alert">{message}</div> : null}
      {successMessage ? <div className="success-alert">{successMessage}</div> : null}

      <div className="permission-template-layout">
        <div className="permission-template-list" role="listbox" aria-label="权限模板目录">
          {templates.map((template) => (
            <button
              key={template.id}
              type="button"
              className={template.id === selectedId ? "permission-template-card selected" : "permission-template-card"}
              onClick={() => selectTemplate(template)}
            >
              <span><strong>{template.name}</strong>{template.isSystem ? <small>内置</small> : null}</span>
              <small>{template.description || "自定义岗位权限"}</small>
            </button>
          ))}
        </div>

        <div className="permission-template-editor">
          {isAdminTemplate ? <div className="permission-readonly-notice">系统管理员模板固定拥有全部功能，不能在此修改。</div> : null}
          <div className="field-grid permission-template-meta-grid">
            <label><span>模板名称</span><input value={draft.name} disabled={isBusy || isAdminTemplate} onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))} /></label>
            <label className="permission-template-count"><span>已开放业务模块</span><strong>{enabledBusinessModuleCount} 个</strong><small>技术支撑权限由系统自动处理</small></label>
            <label className="permission-template-description"><span>说明</span><input value={draft.description} disabled={isBusy || isAdminTemplate} onChange={(event) => setDraft((current) => ({ ...current, description: event.target.value }))} /></label>
            <label className="settings-check"><input type="checkbox" checked={draft.isActive} disabled={isBusy || draft.isSystem} onChange={(event) => setDraft((current) => ({ ...current, isActive: event.target.checked }))} /><span>启用模板</span></label>
          </div>

          <div className="permission-level-guide" aria-label="权限级别说明">
            {accessLevelDescriptions.map((item) => (
              <div key={item.level}><strong>{item.label}</strong><span>{item.description}</span></div>
            ))}
          </div>

          <div className="permission-module-groups">
            {moduleGroups.map(([groupName, modules]) => (
              <div className="permission-module-group" key={groupName}>
                <h3>{groupName}</h3>
                <div className="permission-module-list">
                  {modules.map((module) => (
                    <label className="permission-module-row" key={module.key}>
                      <span><strong>{module.name}</strong></span>
                      <select value={draft.moduleAccess[module.key] ?? ""} disabled={isBusy || isAdminTemplate} onChange={(event) => patchAccess(module.key, event.target.value)}>
                        {["", ...(catalogQuery.data?.accessLevels ?? [])].map((level) => <option key={level || "none"} value={level}>{accessLevelLabels[level] ?? level}</option>)}
                      </select>
                    </label>
                  ))}
                </div>
              </div>
            ))}
          </div>

          <details className="permission-advanced-details">
            <summary>高级信息</summary>
            <label>
              <span>模板代码</span>
              <input value={draft.code} disabled={isBusy || draft.isSystem} onChange={(event) => setDraft((current) => ({ ...current, code: event.target.value }))} />
              <small>用于系统内部识别；一般无需修改。</small>
            </label>
          </details>
        </div>
      </div>
    </section>
  );
}

export default PermissionTemplateManagementPanel;

function createDraftFromTemplate(template: ApiPermissionTemplateDto): TemplateDraft {
  return {
    id: template.id,
    code: template.code,
    name: template.name,
    description: template.description ?? "",
    isSystem: template.isSystem,
    isActive: template.isActive,
    moduleAccess: Object.fromEntries(template.modules.map((module) => [module.moduleKey, module.accessLevel])),
  };
}

function createEmptyDraft(): TemplateDraft {
  return {
    id: 0,
    code: createCustomTemplateCode(),
    name: "新权限模板",
    description: "",
    isSystem: false,
    isActive: true,
    moduleAccess: {},
  };
}

function createCustomTemplateCode() {
  const now = new Date();
  const compact = [
    now.getFullYear(),
    String(now.getMonth() + 1).padStart(2, "0"),
    String(now.getDate()).padStart(2, "0"),
    String(now.getHours()).padStart(2, "0"),
    String(now.getMinutes()).padStart(2, "0"),
  ].join("");
  return `custom-${compact}`;
}
