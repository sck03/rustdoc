import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import type { ApiPermissionResourceDefinitionDto } from "../../api/index.ts";
import { detectPreset, grantKey, presetLabels, scopeLabels } from "./permissionSchemeModel.ts";

export function PermissionModuleGrid({
  resources, grants, dataScopes, disabled, onToggle, onScopeChange, onPresetChange,
}: {
  resources: ApiPermissionResourceDefinitionDto[];
  grants: Record<string, string>;
  dataScopes: string[];
  disabled: boolean;
  onToggle: (resource: ApiPermissionResourceDefinitionDto, action: string, enabled: boolean) => void;
  onScopeChange: (resourceKey: string, action: string, scope: string) => void;
  onPresetChange: (resource: ApiPermissionResourceDefinitionDto, level: string) => void;
}) {
  const [group, setGroup] = useState("");
  const [search, setSearch] = useState("");
  const groups = useMemo(() => [...new Set(resources.map((resource) => resource.group))], [resources]);
  const visible = useMemo(() => {
    const words = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return resources.filter((resource) => (!group || resource.group === group) && words.every((word) =>
      [resource.name, resource.group, ...resource.actions.flatMap((action) => [action.name, action.description])]
        .join(" ").toLowerCase().includes(word)));
  }, [group, resources, search]);

  return (
    <div className="permission-modules">
      <div className="permission-module-toolbar">
        <h3>功能模块 <span>{visible.length} / {resources.length}</span></h3>
        <div className="permission-module-tools">
          <select aria-label="模块分类" value={group} onChange={(event) => setGroup(event.target.value)}>
            <option value="">全部分类</option>
            {groups.map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
          <label className="permission-module-search">
            <Search size={15} aria-hidden="true" />
            <input type="search" aria-label="搜索功能模块" placeholder="搜索模块或操作" maxLength={100} value={search} onChange={(event) => setSearch(event.target.value)} />
          </label>
        </div>
      </div>
      <div className="permission-module-grid">
        {visible.map((resource) => (
          <section className="permission-resource-card" key={resource.key} aria-label={resource.name}>
            <div className="permission-resource-header">
              <h4>{resource.name}</h4>
              <select aria-label={`${resource.name}快捷设置`} disabled={disabled} value={detectPreset(grants, resource)} onChange={(event) => onPresetChange(resource, event.target.value)}>
                <option value="custom" disabled>自定义</option>
                {Object.entries(presetLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
              </select>
            </div>
            <div className="permission-action-list">
              {resource.actions.map((action) => {
                const key = grantKey(resource.key, action.key);
                const enabled = Object.prototype.hasOwnProperty.call(grants, key);
                return (
                  <div className="permission-action-row" key={action.key}>
                    <label className="checkbox-field permission-action-toggle" title={action.description}>
                      <input type="checkbox" aria-label={`${resource.name}：${action.name}`} checked={enabled} disabled={disabled} onChange={(event) => onToggle(resource, action.key, event.target.checked)} />
                      <span>{action.name}</span>
                      <span className="visually-hidden">{action.description}</span>
                    </label>
                    {resource.supportsDataScope ? (
                      <select aria-label={`${resource.name}${action.name}数据范围`} value={grants[key] ?? "own"} disabled={!enabled || disabled} onChange={(event) => onScopeChange(resource.key, action.key, event.target.value)}>
                        {dataScopes.map((scope) => <option key={scope} value={scope}>{scopeLabels[scope] ?? scope}</option>)}
                      </select>
                    ) : <span className="permission-global-scope">全局</span>}
                  </div>
                );
              })}
            </div>
          </section>
        ))}
      </div>
      {visible.length === 0 ? <p className="permission-module-empty">没有匹配的模块，请调整搜索或分类。</p> : null}
    </div>
  );
}
