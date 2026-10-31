import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import type { ApiPermissionResourceDefinitionDto } from "../../api/index.ts";
import { detectPreset, grantKey, initialGrantScope, presetLabels, scopeLabels, setResourcePreset } from "./permissionSchemeModel.ts";
import { filterPermissionResources, permissionResourceLocation } from "./permissionNavigationModel.ts";

export function PermissionModuleGrid({
  resources, grants, dataScopes, disabled, onChange, disabledModules, onModulesChange, inheritedModules = [], readOnlyActions = false, actionDataScopes,
}: {
  resources: ApiPermissionResourceDefinitionDto[];
  grants: Record<string, string>;
  dataScopes: string[];
  disabled: boolean;
  onChange: (grants: Record<string, string>) => void;
  disabledModules: string[];
  onModulesChange: (modules: string[]) => void;
  inheritedModules?: string[];
  readOnlyActions?: boolean;
  actionDataScopes?: Record<string, string[]>;
}) {
  const [selectedGroup, setGroup] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const groups = useMemo(() => [...new Set(resources.map((resource) => permissionResourceLocation(resource).group))], [resources]);
  const group = selectedGroup === "" ? "" : groups.includes(selectedGroup ?? "") ? selectedGroup! : groups[0] ?? "";
  const visible = useMemo(() => filterPermissionResources(resources, group, search), [group, resources, search]);

  return (
    <div className="permission-modules">
      <div className="permission-module-toolbar">
        <h3>功能模块 <span>显示 {visible.length} / 共 {resources.length}</span></h3>
        <div className="permission-module-tools">
          <select aria-label="模块分类" value={group} onChange={(event) => setGroup(event.target.value)}>
            <option value="">全部分类</option>
            {groups.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
          <label className="permission-module-search">
            <Search size={15} aria-hidden="true" />
            <input type="search" aria-label="搜索功能模块" placeholder="跨分类搜索模块或操作" maxLength={100} value={search} onChange={(event) => { setSearch(event.target.value); setGroup(event.target.value.trim() ? "" : null); }} />
          </label>
        </div>
      </div>
      <details className="permission-module-help"><summary>模块开关与操作权限如何配合</summary>
        <p className="permission-navigation-note">关闭模块同时隐藏入口并拒绝后台访问，保留勾选配置以便重新开放。同一页面的模块开关同步生效；各项操作独立设置数据范围，筛选不会丢失草稿。</p>
        <p className="permission-navigation-note">查看、日常操作和管理分别授权，管理不会自动取得编辑权限。使用页面通常还需勾选“查看”，查看范围应覆盖需办理的记录；公司范围的查看不代表可以编辑同事的数据。</p>
      </details>
      <div className="permission-module-grid">
        {visible.map((resource) => (
          <section className="permission-resource-card" key={resource.key} aria-label={resource.name}>
            <p className="permission-resource-location">{permissionResourceLocation(resource).path}<span>{disabledModules.includes(resource.moduleKey) ? "模块已关闭" : `${resource.actions.filter(action => grants[grantKey(resource.key, action.key)]).length} / ${resource.actions.length} 项已授权`}</span></p>
            <label className="checkbox-field"><input type="checkbox" aria-label={`${resource.name}模块开放`} checked={!disabledModules.includes(resource.moduleKey)} disabled={disabled || inheritedModules.includes(resource.moduleKey)}
              onChange={event => onModulesChange(event.target.checked ? disabledModules.filter(key => key !== resource.moduleKey) : [...disabledModules, resource.moduleKey])} /><span>开放模块（仍需勾选操作）</span></label>
            {inheritedModules.includes(resource.moduleKey) && <p className="field-help">权限分组已关闭此模块，须先在分组中开放。</p>}
            <div className="permission-resource-header">
              <h4>{resource.name}</h4>
              <select aria-label={`${resource.name}快捷设置`} disabled={disabled || readOnlyActions || disabledModules.includes(resource.moduleKey)} value={detectPreset(grants, resource)} onChange={(event) => onChange(setResourcePreset(grants, resource, event.target.value, actionDataScopes))}>
                <option value="custom" disabled>自定义</option>
                {Object.entries(presetLabels).filter(([value]) => value !== "approve" || resource.actions.some(action => action.key === "approve"))
                  .map(([value, label]) => <option key={value} value={value}>{value==="approve"&&!resource.workflowResource?"查看与确认":label}</option>)}
              </select>
            </div>
            <div className="permission-action-list">
              {resource.actions.map((action) => {
                const key = grantKey(resource.key, action.key);
                const enabled = Object.prototype.hasOwnProperty.call(grants, key);
                return (
                  <div className="permission-action-row" key={action.key}>
                    <label className="checkbox-field permission-action-toggle" title={action.description}>
                      <input type="checkbox" aria-label={`${resource.name}：${action.name}`} checked={enabled} disabled={disabled || readOnlyActions || disabledModules.includes(resource.moduleKey)} onChange={(event) => {
                        const next = { ...grants };
                        if (event.target.checked) next[key] = initialGrantScope(resource, key, actionDataScopes);
                        else delete next[key];
                        onChange(next);
                      }} />
                      <span>{action.name}</span>
                      <span className="visually-hidden">{action.description}</span>
                    </label>
                    {resource.supportsDataScope ? (
                      <select aria-label={`${resource.name}${action.name}数据范围`} value={grants[key] ?? "own"} disabled={!enabled || disabled || readOnlyActions || disabledModules.includes(resource.moduleKey)} onChange={(event) => onChange({ ...grants, [key]: event.target.value })}>
                        {(actionDataScopes?.[key] ?? dataScopes).map((scope) => <option key={scope} value={scope}>{scopeLabels[scope] ?? scope}</option>)}
                      </select>
                    ) : <span className="permission-global-scope" title={resource.scopeDescription}>{resource.unitAssignable ? "业务边界" : "功能授权"}</span>}
                  </div>
                );
              })}
            </div>
            {!disabledModules.includes(resource.moduleKey) && resource.actions.some(action => action.key === "view") &&
              !grants[grantKey(resource.key, "view")] && resource.actions.some(action => grants[grantKey(resource.key, action.key)]) &&
              <p className="field-help">已配置操作但未配置查看，页面或记录可能无法打开。请核对查看权限及其数据范围。</p>}
            {resource.workflowResource && !disabledModules.includes(resource.moduleKey) && grants[grantKey(resource.key, "approve")] === "own" &&
              <p className="field-help">可审批明确分派给自己的同公司申请。普通资料范围独立配置，无须为了审批扩大查看范围。</p>}
          </section>
        ))}
      </div>
      {visible.length === 0 ? <p className="permission-module-empty">没有匹配的模块，请调整搜索或分类。</p> : null}
    </div>
  );
}
