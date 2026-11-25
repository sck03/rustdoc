import type { ApiUserDto, OaRequest } from "../../api/index.ts";

export function canReassignApproval(user: ApiUserDto, row: { status: string; approvalPlan?: OaRequest["approvalPlan"] }) {
  return Boolean((user.capabilities.canManageUsers || user.capabilities.unitManagement?.workflows)
    && ["Pending", "Suspended"].includes(row.status)
    && row.approvalPlan?.steps.some(step => step.status === "Waiting"));
}
