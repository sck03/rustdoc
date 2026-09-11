import type { ApiPermissionResourceDefinitionDto } from "../../api/index.ts";
import { workspaceNavGroups } from "../../app/workspaceNavigationCatalog.ts";

const relatedModules: Record<string, string> = {
  "document.invoice-reports": "document.invoices",
  "document.payment-reports": "document.payments",
};

export function permissionResourceLocation(resource: ApiPermissionResourceDefinitionDto) {
  const moduleKey = relatedModules[resource.moduleKey] ?? resource.moduleKey;
  const locations = workspaceNavGroups.flatMap((group) => group.items.flatMap((item) =>
    (item.children ?? [item]).filter((route) => route.moduleKey === moduleKey).map(() => ({ group: group.label, page: item.label }))));
  const group = locations[0]?.group ?? resource.group;
  const pages = [...new Set(locations.map((location) => location.page))];
  return { group, path: [group, pages.join(" / ")].filter(Boolean).join(" / ") };
}

export function filterPermissionResources(resources: ApiPermissionResourceDefinitionDto[], group: string, search: string) {
  const words = search.normalize("NFKC").trim().toLowerCase().split(/\s+/).filter(Boolean);
  return resources.filter((resource) => {
    const location = permissionResourceLocation(resource);
    const text = [resource.name, location.path, resource.group, ...resource.actions.flatMap((action) => [action.name, action.description])]
      .join(" ").normalize("NFKC").toLowerCase();
    return (!group || location.group === group) && words.every((word) => text.includes(word));
  });
}
