import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiPermissionGrantDto, ApiPermissionTemplateOptionDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { PermissionModuleGrid } from "./PermissionModuleGrid.tsx";
import { getEditableSchemeGrants, toPermissionGrants } from "./permissionSchemeModel.ts";

export function UserPermissionEditor({ client, template, grants, disabledModules, busy, onChange }: {
  client: ExportDocManagerApiClient;
  template?: ApiPermissionTemplateOptionDto;
  grants: ApiPermissionGrantDto[] | null;
  disabledModules: string[];
  busy: boolean;
  onChange: (grants: ApiPermissionGrantDto[] | null, disabledModules: string[]) => void;
}) {
  const [expanded, setExpanded] = useState(grants !== null || disabledModules.length > 0);
  const catalog = useQuery({ queryKey: [...queryKeys.permissionTemplates(), "account", template?.id ?? null], queryFn: ({ signal }) => client.listPermissionTemplates({ signal }) });
  const resources = catalog.data?.resources ?? [];
  const source = catalog.data?.templates.find(item => item.id === template?.id);
  const resourceByKey = useMemo(() => new Map(resources.map(resource => [resource.key, resource])), [resources]);
  const inheritedModules = grants === null ? source?.disabledModules ?? [] : [];
  const effectiveModules = [...new Set([...disabledModules, ...inheritedModules])];
  const editable = getEditableSchemeGrants(grants ?? (source?.isActive ? source.grants : []), resourceByKey);
  return <details className="permission-effective-details" open={expanded} onToggle={event => setExpanded(event.currentTarget.open)}>
    <summary>账号单独配置与模块权限{grants === null ? "（继承分组）" : "（独立配置）"}</summary>
    <p>日常建议共享权限分组。单独配置会复制当前分组的可授权操作，此后不随分组更新；没有可复制的分组时从空权限开始。空权限表示不开放业务，恢复继承后重新使用岗位或所选分组。</p>
    {catalog.isError ? <InlineNotice tone="error">{readApiError(catalog.error)}</InlineNotice> : null}
    <label><span>权限来源</span><select aria-label="账号权限来源" value={grants === null ? "inherit" : "custom"} disabled={busy || !catalog.data}
      onChange={event => event.target.value === "inherit" ? onChange(null, disabledModules) : onChange(
        source?.isActive === false ? [] : toPermissionGrants(getEditableSchemeGrants(source?.grants ?? [], resourceByKey)), [...new Set([...disabledModules, ...(source?.disabledModules ?? [])])])}>
      <option value="inherit">继承岗位或共享分组</option><option value="custom">账号单独配置</option>
    </select></label>
    {grants === null && <p>当前来源：{template?.name ?? "角色默认权限"}。操作和数据范围随分组更新；可在下方为本账号单独关闭模块，重新开放仍受分组限制。{source?.isActive === false && "当前分组已停用，全部业务权限已关闭。"}</p>}
    <PermissionModuleGrid resources={resources.filter(resource => !resource.isTechnical)} grants={editable}
      dataScopes={catalog.data?.dataScopes ?? []} disabled={busy || !catalog.data}
      onChange={next => onChange(toPermissionGrants(next), disabledModules)} disabledModules={effectiveModules}
      inheritedModules={inheritedModules} readOnlyActions={grants === null}
      onModulesChange={modules => onChange(grants, [...new Set([...modules.filter(key => !inheritedModules.includes(key)), ...disabledModules.filter(key => inheritedModules.includes(key))])])} />
  </details>;
}
