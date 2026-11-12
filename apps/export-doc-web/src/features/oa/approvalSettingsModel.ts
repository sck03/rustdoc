import type { OaApprovalRule } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { oaModules, personnelChangeLabels } from "./oaModel.ts";
import { newFormField } from "./oaFormModel.ts";

export function ruleLabel(rule: OaApprovalRule) {
  if (rule.form) return rule.name;
  const label = rule.kind === "room" ? "会议室预约" : rule.kind === "supply" ? "物品领用" : oaModules[rule.kind].name;
  return (
    label + (rule.variant ? ` · ${personnelChangeLabels[rule.variant as keyof typeof personnelChangeLabels]}` : "")
  );
}

export function createApplication(template: OaApprovalRule, copy: boolean): OaApprovalRule {
  const variant = createRequestKey()
    .replace(/[^a-zA-Z0-9_]/g, "")
    .slice(0, 40);
  const source = structuredClone(template);
  return {
    ...source,
    key: `general:${variant}`,
    variant,
    kind: "general",
    name: copy ? `${source.name}副本` : "新审批事项",
    form: source.form ?? { category: "自定义事项", fields: [newFormField()] },
  };
}
