import type { ApiPermissionGrantDto, ApiPermissionResourceDefinitionDto } from "../../api/index.ts";
import { dataScopeOrder, grantKey, toPermissionGrants } from "./permissionSchemeModel.ts";

const recordScopes = dataScopeOrder.filter(scope => scope !== "all");
export function unitPermissionEditor(catalog: ApiPermissionResourceDefinitionDto[], ceiling: ApiPermissionGrantDto[] | null | undefined, collaboration = false) {
  const bounds = ceiling == null ? null : new Map(ceiling.map(grant => [grantKey(grant.resourceKey, grant.action), grant.dataScope]));
  const directory = new Map(catalog.map(resource => [resource.key, resource]));
  const resources = catalog.filter(resource => resource.unitAssignable === true).map(resource => ({
    ...resource,
    actions: resource.actions.filter(action => action.unitAssignable === true
      && (!bounds || bounds.has(grantKey(resource.key, action.key)))
      && (!collaboration || resource.workflowResource === true && ["view", "approve"].includes(action.key))),
  })).filter(resource => resource.actions.length);
  const actionDataScopes = Object.fromEntries(resources.flatMap(resource => resource.actions.map(action => {
    const key = grantKey(resource.key, action.key), maximum = bounds?.get(key);
    return [key, resource.supportsDataScope
      ? recordScopes.filter(scope => !maximum || dataScopeOrder.indexOf(scope) <= dataScopeOrder.indexOf(maximum))
      : ["all"]];
  })));
  function grants(value: Record<string, string>) {
    return toPermissionGrants(value).map(grant => {
      const resource = directory.get(grant.resourceKey);
      if (!resource) return grant;
      if (!resource.supportsDataScope) return { ...grant, dataScope: "all" };
      const choices = actionDataScopes[grantKey(grant.resourceKey, grant.action)] ?? [];
      const requested = grant.dataScope === "all" ? "company" : grant.dataScope;
      return { ...grant, dataScope: choices.includes(requested) ? requested : choices.at(-1) ?? requested };
    });
  }
  function collaborationScheme(grants: ApiPermissionGrantDto[]) {
    return grants.every(grant => directory.get(grant.resourceKey)?.workflowResource === true && ["view", "approve"].includes(grant.action));
  }
  return { resources, actionDataScopes, grants, recordScopes, collaborationScheme };
}
