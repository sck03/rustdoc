import type { OaApprovalSettings } from "../../api/index.ts";

export const editableFields = ["rules", "delegations", "handlingServices", "sheetTemplates", "calendar"] as const;
export type ApprovalSettingsField = typeof editableFields[number];

/** Only the submitted section may advance a draft's concurrency baseline. */
export function acceptApprovalSection(saved: OaApprovalSettings, submitted: OaApprovalSettings, field?: ApprovalSettingsField, definitionKey?: string): OaApprovalSettings {
  const result = {
    ...saved, ...Object.fromEntries(editableFields.map(key => [key, submitted[key]])),
    revisions: { ...submitted.revisions },
  };
  if (field === "rules") {
    result.rules = submitted.rules.map(rule => rule.key === definitionKey ? saved.rules.find(item => item.key === definitionKey) ?? rule : rule);
    result.revisions[`rule:${definitionKey}`] = saved.revisions?.[`rule:${definitionKey}`] ?? 0;
  } else if (field) {
    Object.assign(result, { [field]: saved[field] });
    result.revisions[field] = saved.revisions?.[field] ?? 0;
  }
  return result;
}
