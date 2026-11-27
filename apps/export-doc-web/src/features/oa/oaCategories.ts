import type { OaApprovalRule, OaLeave } from "../../api/index.ts";

export const leaveCategories = { Annual: "年假", Sick: "病假", Personal: "事假", Other: "其他" } satisfies Record<OaLeave["category"], string>;
import { generalCategories } from "../../api/generated/generalCategories.ts";
export { generalCategories };

export const defaultLeaveCategory = "Annual";
export const defaultGeneralCategory = "Other";

export function workflowCategoryField(rule?: Pick<OaApprovalRule, "kind" | "form">): {
  label: string;
  options: Readonly<Record<string, string>>;
  defaultValue: string;
} | undefined {
  if (rule?.form) return undefined;
  switch (rule?.kind ?? "general") {
    case "general":
      return { label: "申请类别", options: generalCategories, defaultValue: defaultGeneralCategory };
    case "leave":
      return { label: "请假类型", options: leaveCategories, defaultValue: defaultLeaveCategory };
  }
}
