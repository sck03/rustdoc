import { useMemo } from "react";
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
  const catalog = useQuery({ queryKey: [...queryKeys.permissionTemplates(), "account", template?.id ?? null], queryFn: ({ signal }) => client.listPermissionTemplates({ signal }) });
  const resources = catalog.data?.resources ?? [];
  const source = catalog.data?.templates.find(item => item.id === template?.id);
  const resourceByKey = useMemo(() => new Map(resources.map(resource => [resource.key, resource])), [resources]);
  const editable = getEditableSchemeGrants(grants ?? [], resourceByKey);
  return <details className="permission-effective-details" open={grants !== null || undefined}>
    <summary>账号单独配置与模块权限{grants === null ? "（继承分组）" : "（独立配置）"}</summary>
    <p>日常建议共享权限分组。单独配置会复制当前分组的可授权操作，此后不随分组更新；没有可复制的分组时从空权限开始。空权限表示不开放业务，恢复继承后重新使用岗位或所选分组。</p>
    {catalog.isError ? <InlineNotice tone="error">{readApiError(catalog.error)}</InlineNotice> : null}
    <label><span>权限来源</span><select aria-label="账号权限来源" value={grants === null ? "inherit" : "custom"} disabled={busy || !catalog.data}
      onChange={event => event.target.value === "inherit" ? onChange(null, []) : onChange(
        source?.isActive === false ? [] : toPermissionGrants(getEditableSchemeGrants(source?.grants ?? [], resourceByKey)), source?.disabledModules ?? [])}>
      <option value="inherit">继承岗位或共享分组</option><option value="custom">账号单独配置</option>
    </select></label>
    {grants !== null ? <PermissionModuleGrid resources={resources.filter(resource => !resource.isTechnical)} grants={editable}
      dataScopes={catalog.data?.dataScopes ?? []} disabled={busy || !catalog.data}
      onChange={next => onChange(toPermissionGrants(next), disabledModules)} disabledModules={disabledModules}
      onModulesChange={modules => onChange(grants, modules)} /> : <p>当前来源：{template?.name ?? "岗位默认权限"}。在“权限方案”中维护共享分组，再将账号分配到该分组。</p>}
  </details>;
}
