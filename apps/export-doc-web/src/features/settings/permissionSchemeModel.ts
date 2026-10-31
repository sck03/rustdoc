import type { ApiPermissionGrantDto, ApiPermissionResourceDefinitionDto, ApiPermissionTemplateDto } from "../../api/index.ts";

export function getEditableSchemeGrants(grants: ApiPermissionGrantDto[], resources: ReadonlyMap<string, ApiPermissionResourceDefinitionDto>) {
  return Object.fromEntries(grants
    .filter((grant) => resources.get(grant.resourceKey)?.isTechnical !== true)
    .map((grant) => [grantKey(grant.resourceKey, grant.action), grant.dataScope]));
}

export const scopeLabels: Record<string, string> = {
  own: "本人",
  department: "部门",
  company: "公司",
  all: "全部",
};
export const dataScopeOrder: readonly string[] = ["own", "department", "company", "all"];

export const presetLabels: Record<string, string> = {
  "": "未授权",
  view: "仅查看",
  approve: "查看与审批",
  operate: "日常操作",
  manage: "完整管理",
};

export const presetRanks: Record<string, number> = { view: 1, operate: 2, manage: 3 };

/** OA schemes operate within one unit; group reporting has a separate authorization. */
export function schemeActionDataScopes(resources: ApiPermissionResourceDefinitionDto[], scopes: string[]) {
  return Object.fromEntries(resources.flatMap(resource => resource.actions.map(action => [grantKey(resource.key,action.key),resource.workspace === "office" && resource.supportsDataScope ? scopes.filter(scope => scope !== "all") : scopes])));
}

function presetActions(resource: ApiPermissionResourceDefinitionDto, level: string) {
  if (level === "approve") return resource.actions.some(action => action.key === "approve")
    ? resource.actions.filter(action => action.key === "view" || action.key === "approve") : [];
  const rank = presetRanks[level];
  return rank ? resource.actions.filter(action => (presetRanks[action.presetLevel] ?? Infinity) <= rank) : [];
}

export function grantKey(resourceKey: string, action: string) {
  return `${resourceKey}\u001f${action}`;
}

export function splitGrantKey(key: string) {
  const [resourceKey = "", action = ""] = key.split("\u001f", 2);
  return [resourceKey, action] as const;
}

export function initialGrantScope(resource: ApiPermissionResourceDefinitionDto, key: string, actionDataScopes?: Record<string, string[]>) {
  if (!resource.supportsDataScope) return "all";
  const choices = actionDataScopes?.[key];
  return choices && !choices.includes("own") ? choices[0] ?? "own" : "own";
}

export function setResourcePreset(current: Record<string, string>, resource: ApiPermissionResourceDefinitionDto, level: string, actionDataScopes?: Record<string, string[]>) {
  const grants = Object.fromEntries(Object.entries(current).filter(([key]) => splitGrantKey(key)[0] !== resource.key));
  for (const action of presetActions(resource, level)) {
    const key = grantKey(resource.key, action.key);
    grants[key] = current[key] ?? initialGrantScope(resource, key, actionDataScopes);
  }
  return grants;
}

export function toPermissionGrants(grants: Record<string, string>): ApiPermissionGrantDto[] {
  return Object.entries(grants).map(([key, dataScope]) => {
    const [resourceKey, action] = splitGrantKey(key);
    return { resourceKey, action, dataScope };
  });
}

export function detectPreset(grants: Record<string, string>, resource: ApiPermissionResourceDefinitionDto) {
  const enabled = resource.actions.filter((action) => Object.prototype.hasOwnProperty.call(grants, grantKey(resource.key, action.key)));
  if (enabled.length === 0) return "";
  for (const level of ["view", "approve", "operate", "manage"]) {
    const expected = presetActions(resource, level);
    if (expected.length === enabled.length && expected.every((action) => enabled.includes(action))) return level;
  }
  return "custom";
}

export const schemeStartingPoints = { blank: "空白方案", employee: "普通员工方案", department: "部门审批负责人", company: "公司审批负责人", combined: "组合多个职责方案" } as const;
export type SchemeStartingPoint = keyof typeof schemeStartingPoints;

export function createSchemeFromEmployee(source: ApiPermissionTemplateDto, resources: ReadonlyMap<string, ApiPermissionResourceDefinitionDto>, scope: "employee" | "department" | "company") {
  const grants = getEditableSchemeGrants(source.grants, resources);
  if (scope !== "employee") {
    for (const resource of resources.values()) {
      if (resource.isTechnical || !resource.workflowResource) continue;
      for (const action of presetActions(resource, "approve")) grants[grantKey(resource.key, action.key)] = scope;
    }
  }
  return { grants, disabledModules: [...(source.disabledModules ?? [])], oaAssignedOnly: source.oaAssignedOnly ?? false };
}

export function combinePermissionSchemes(sources: ApiPermissionTemplateDto[], resources: ReadonlyMap<string, ApiPermissionResourceDefinitionDto>) {
  const grants: Record<string, string> = {};
  const ranks = dataScopeOrder;
  for (const source of sources) {
    for (const [key, scope] of Object.entries(getEditableSchemeGrants(source.grants, resources))) {
      if (!(key in grants) || ranks.indexOf(scope) > ranks.indexOf(grants[key])) grants[key] = scope;
    }
  }
  return { grants, disabledModules: [...new Set(sources.flatMap(source => source.disabledModules ?? []))], oaAssignedOnly: sources.some(source => source.oaAssignedOnly) };
}
