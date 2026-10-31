import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiPermissionGrantDto, ApiPermissionTemplateOptionDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { PermissionModuleGrid } from "./PermissionModuleGrid.tsx";
import { getEditableSchemeGrants, toPermissionGrants, schemeActionDataScopes } from "./permissionSchemeModel.ts";

export function UserPermissionEditor({ client, template, schemeVersion, grants, disabledModules, oaAssignedOnly, busy, onChange: update }: {
  client: ExportDocManagerApiClient;
  template?: ApiPermissionTemplateOptionDto;
  schemeVersion?: number | null;
  grants: ApiPermissionGrantDto[] | null;
  disabledModules: string[];
  oaAssignedOnly: boolean;
  busy: boolean;
  onChange: (grants: ApiPermissionGrantDto[] | null, disabledModules: string[], oaAssignedOnly: boolean) => void;
}) {
  const [expanded, setExpanded] = useState(grants !== null || disabledModules.length > 0 || oaAssignedOnly);
  const [opened, setOpened] = useState(expanded);
  const catalog = useQuery({ queryKey: queryKeys.permissionTemplates(), enabled: opened, queryFn: ({ signal }) => client.listPermissionTemplates({ signal }) });
  const resources = catalog.data?.resources ?? [];
  const definition = useQuery({ queryKey: ["permission-version-definition",template?.id,schemeVersion],enabled:opened&&Boolean(template?.id&&schemeVersion),queryFn:({signal})=>client.getPermissionSchemeVersion({schemeId:String(template?.id),version:schemeVersion??0},{signal}) });
  const source = definition.data;
  const inheritedRestriction = grants === null && source?.oaAssignedOnly === true;
  const onChange = (nextGrants: ApiPermissionGrantDto[] | null, modules: string[]) => update(nextGrants, modules, oaAssignedOnly || inheritedRestriction);
  const resourceByKey = useMemo(() => new Map(resources.map(resource => [resource.key, resource])), [resources]);
  const inheritedModules = grants === null ? source?.disabledModules ?? [] : [];
  const effectiveModules = [...new Set([...disabledModules, ...inheritedModules])];
  const editable = getEditableSchemeGrants(grants ?? (source?.isActive ? source.grants : []), resourceByKey);
  return <details className="permission-effective-details" open={expanded} onToggle={event => {
    setExpanded(event.currentTarget.open);
    if (event.currentTarget.open) setOpened(true);
  }}>
    <summary>账号单独配置与模块权限{grants === null ? "（继承分组）" : "（独立配置）"}</summary>
    {opened && <>
    <p>日常建议共享权限分组。单独配置会复制当前分组的可授权操作，此后不随分组更新；没有可复制的分组时从空权限开始。空权限表示不开放业务，恢复继承后重新使用所选权限方案。</p>
    {catalog.isError ? <InlineNotice tone="error">{readApiError(catalog.error)}</InlineNotice> : null}
    {definition.isError&&<InlineNotice tone="error">{readApiError(definition.error)}</InlineNotice>}
    <label className="checkbox-field"><input type="checkbox" checked={oaAssignedOnly || inheritedRestriction} disabled={busy || inheritedRestriction} onChange={event => update(grants, disabledModules, event.target.checked)} />仅查看本人及分派给自己的申请{inheritedRestriction ? "（继承自方案）" : ""}</label>
    <p>此限制同时用于申请列表、详情、附件、通知和待办，仍受普通数据范围约束。需要查看本公司全部申请的公司领导审批负责人 不应启用。</p>
    <label><span>权限来源</span><select aria-label="账号权限来源" value={grants === null ? "inherit" : "custom"} disabled={busy || !catalog.data}
      onChange={event => event.target.value === "inherit" ? onChange(null, disabledModules) : onChange(
        source?.isActive === false ? [] : toPermissionGrants(getEditableSchemeGrants(source?.grants ?? [], resourceByKey)), [...new Set([...disabledModules, ...(source?.disabledModules ?? [])])])}>
      <option value="inherit">使用共享权限方案</option><option value="custom">账号单独配置</option>
    </select></label>
    {grants === null && <p>当前来源：{source?.name??template?.name ?? "待选择权限方案"} · 发布版本 {schemeVersion??"待选择"}。草稿保存不改变此身份权限；明确应用新版本后才更新。{source?.isActive === false && "此发布版本已关闭业务权限。"}</p>}
    <PermissionModuleGrid resources={resources.filter(resource => !resource.isTechnical)} grants={editable}
      dataScopes={catalog.data?.dataScopes ?? []} disabled={busy || !catalog.data}
      actionDataScopes={schemeActionDataScopes(resources,catalog.data?.dataScopes ?? [])}
      onChange={next => onChange(toPermissionGrants(next), disabledModules)} disabledModules={effectiveModules}
      inheritedModules={inheritedModules} readOnlyActions={grants === null}
      onModulesChange={modules => onChange(grants, [...new Set([...modules.filter(key => !inheritedModules.includes(key)), ...disabledModules.filter(key => inheritedModules.includes(key))])])} />
    </>}
  </details>;
}
