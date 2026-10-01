import type { ApiPermissionGrantDto, ApiPermissionResourceDefinitionDto } from "../../api/index.ts";

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

export const presetLabels: Record<string, string> = {
  "": "未开放",
  view: "仅查看",
  operate: "日常操作",
  manage: "完整管理",
};

export const presetRanks: Record<string, number> = { view: 1, operate: 2, manage: 3 };

export function grantKey(resourceKey: string, action: string) {
  return `${resourceKey}\u001f${action}`;
}

export function splitGrantKey(key: string) {
  const [resourceKey = "", action = ""] = key.split("\u001f", 2);
  return [resourceKey, action] as const;
}

export function firstResourceScope(grants: Record<string, string>, resourceKey: string) {
  return Object.entries(grants).find(([key]) => splitGrantKey(key)[0] === resourceKey)?.[1] ?? "";
}

export function setResourcePreset(current: Record<string, string>, resource: ApiPermissionResourceDefinitionDto, level: string) {
  const grants = Object.fromEntries(Object.entries(current).filter(([key]) => splitGrantKey(key)[0] !== resource.key));
  const scope = firstResourceScope(current, resource.key) || (resource.supportsDataScope ? "own" : "all");
  for (const action of resource.actions) {
    if (level && (presetRanks[action.presetLevel] ?? 0) <= (presetRanks[level] ?? 0)) grants[grantKey(resource.key, action.key)] = scope;
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
  for (const level of ["view", "operate", "manage"]) {
    const expected = resource.actions.filter((action) => (presetRanks[action.presetLevel] ?? 0) <= presetRanks[level]);
    if (expected.length === enabled.length && expected.every((action) => enabled.includes(action))) return level;
  }
  return "custom";
}
