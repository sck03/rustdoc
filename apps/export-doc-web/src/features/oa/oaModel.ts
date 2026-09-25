import type { ApiUserDto, OaRequest, OaRequestSave } from "../../api/index.ts";
import { hasPermission } from "../../app/PermissionAccessContext.tsx";
import { officeBookingDefaults } from "../office/officeModel.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";

export type OaKind = OaRequest["kind"];
export const oaKinds: OaKind[] = ["leave", "overtime", "expense", "travel", "purchase", "general"];
export const oaModules = {
  leave: { name: "员工请假", resource: "office.leave", description: "按半天申请，审批后销假归档；自然日时长不自动扣减年假余额。" },
  overtime: { name: "加班申请", resource: "office.overtime", description: "记录加班事由和时段，审批后确认实际完成。" },
  expense: { name: "费用报销", resource: "office.expenses", description: "提交费用明细和 PDF／图片凭证，审批通过后移交独立财务软件。" },
  travel: { name: "出差申请", resource: "office.travel", description: "登记出差地点、日期和事由，返程后完成归档。" },
  purchase: { name: "采购申请", resource: "office.purchase", description: "提交物品、数量和预算，审批后登记采购验收结果。" },
  general: { name: "通用申请", resource: "office.general", description: "办理用印、证明、IT 支持和维修申请，保留审批与办结记录。" },
} as const;
export const oaStatus: Record<OaRequest["status"], string> = { Draft: "草稿", Pending: "待审批", Approved: "已批准", Rejected: "已驳回", Cancelled: "已取消", Completed: "已完成", HandedOff: "已移交财务" };
export const oaActionLabels = { submit: "提交审批", withdraw: "撤回修改", approve: "批准", reject: "驳回", cancel: "取消申请", void: "作废批准", complete: "完成登记" } as const;
export type OaActionName = keyof typeof oaActionLabels;
export function oaAccess(user: ApiUserDto, kind: OaKind, action: string, row?: OaRequest) {
  const resource = oaModules[kind].resource;
  if (!hasPermission(user.capabilities.permissions, resource, action)) return false;
  return !row || user.capabilities.permissions.some((grant) => grant.resourceKey === resource && grant.action === action &&
    (grant.dataScope === "all" || grant.dataScope === "company" || grant.dataScope === "department" && row.departmentId === user.departmentId || grant.dataScope === "own" && row.ownerUserId === user.id));
}
export function oaActions(row: OaRequest, user: ApiUserDto): OaActionName[] {
  const allows = (action: string) => oaAccess(user, row.kind, action, row);
  const reviewer = user.capabilities.usesOfficeRegister || row.ownerUserId !== user.id;
  if (row.status === "Draft" || row.status === "Rejected") return [...(allows("edit") ? ["submit" as const] : []), ...(allows("cancel") ? ["cancel" as const] : [])];
  if (row.status === "Pending") return [...(allows("edit") ? ["withdraw" as const] : []), ...(allows("approve") && reviewer ? ["approve" as const, "reject" as const] : [])];
  if (row.status === "Approved") return [...(allows("complete") ? ["complete" as const] : []), ...(allows("approve") && reviewer ? ["void" as const] : [])];
  return [];
}
export function oaActionLabel(action: OaActionName, kind: OaKind, local: boolean) {
  if (action === "complete") return ({ expense: "移交财务", purchase: "登记验收", leave: "销假归档", travel: "返程归档", overtime: "确认加班完成", general: "办结登记" } as const)[kind];
  if (action === "approve" && local) return "登记批准结果";
  return oaActionLabels[action];
}
export function oaDraft(kind: OaKind, user: ApiUserDto): OaRequestSave {
  const { start, end } = officeBookingDefaults(user.businessTimeZone);
  return { requestKey: createRequestKey(), title: "", reason: "", currency: "CNY",
    ...(kind === "leave" ? { leave: { category: "Annual", startsOn: user.businessDate, endsOn: user.businessDate, startPeriod: "AM", endPeriod: "PM" } } : {}),
    ...(kind === "expense" ? { lines: [{ category: "Travel", spentOn: user.businessDate, description: "", amount: "" }] } : {}),
    ...(kind === "purchase" ? { purchaseLines: [{ name: "", specification: "", quantity: "1", unit: "件", unitPrice: "" }] } : {}),
    ...(kind === "travel" ? { travel: { destination: "", startsOn: user.businessDate, endsOn: user.businessDate } } : {}),
    ...(kind === "overtime" ? { overtime: { startsAt: start, endsAt: end, location: "" } } : {}),
    ...(kind === "general" ? { category: "Other" } : {}) };
}
