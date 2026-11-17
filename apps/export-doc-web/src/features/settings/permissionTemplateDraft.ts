import type { ApiPermissionTemplateDto, ApiPermissionResourceDefinitionDto } from "../../api/index.ts";
import { getEditableSchemeGrants } from "./permissionSchemeModel.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";

export type TemplateDraft = {
  id: number;
  versionNumber: number;
  code: string;
  name: string;
  description: string;
  isSystem: boolean;
  isActive: boolean;
  grants: Record<string, string>;
  disabledModules: string[];
  oaAssignedOnly: boolean;
};

export function createDraftFromTemplate(template: ApiPermissionTemplateDto, resources: ReadonlyMap<string, ApiPermissionResourceDefinitionDto>): TemplateDraft {
  return {
    id: template.id,
    versionNumber: template.versionNumber ?? 1,
    code: template.code,
    name: template.name,
    description: template.description ?? "",
    isSystem: template.isSystem,
    isActive: template.isActive,
    grants: getEditableSchemeGrants(template.grants, resources),
    disabledModules: template.disabledModules ?? [],
    oaAssignedOnly: template.oaAssignedOnly ?? false,
  };
}

export function createEmptyDraft(): TemplateDraft {
  return { id: 0, versionNumber: 0, code: createCustomTemplateCode(), name: "新权限方案", description: "", isSystem: false, isActive: true, grants: {}, disabledModules: [], oaAssignedOnly: false };
}

export function buildTemplateDraftSnapshot(draft: TemplateDraft) {
  return JSON.stringify({ ...draft, grants: Object.fromEntries(Object.entries(draft.grants).sort(([left], [right]) => left.localeCompare(right))) });
}

export function createCustomTemplateCode() {
  return `custom-${createRequestKey()}`;
}
