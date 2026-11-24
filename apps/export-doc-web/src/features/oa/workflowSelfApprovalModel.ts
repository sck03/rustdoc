import type { OaApprovalStage } from "../../api/index.ts";

// Describes the existing routing settings; the server resolves people and validates the route.
export function departmentSelfRouting(stage: OaApprovalStage) {
  if ((stage.kind ?? "Approval") !== "Approval" || stage.source !== "DepartmentManager") return null;
  if (stage.selfApproval !== "Skip") return { skips: false, label: "负责人本人申请须补充责任人" };
  if (stage.repeatedApprover === "Reject" || stage.revisionPolicy?.protectedUserIds?.length) {
    return { skips: false, label: "独立或必经复核，负责人本人不能跳过" };
  }
  return { skips: true, label: "负责人本人跳过，转公司领导审批" };
}
