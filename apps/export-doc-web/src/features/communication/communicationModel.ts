import type { Announcement, ApiUserDto, SiteNotification } from "../../api/index.ts";
import { hasPermission } from "../../app/PermissionAccessContext.tsx";
import { oaKinds } from "../oa/oaModel.ts";

export const announcementStatus = { Draft: "草稿", Published: "已发布", Withdrawn: "已撤回", Archived: "已归档" };
export const notificationActions: Record<string, string> = { submit: "待您审批", "approve-step": "下一步待您审批", "handling-step":"办理进度已更新", remind: "申请人催办", approve: "审批通过", reject: "审批驳回", complete: "已办结 / 移交", void: "批准已作废", reassign: "办理分工已交接", issue: "已发放 / 交接", return: "已登记归还", cancel: "申请已取消" };
export function notificationActionLabel(row: SiteNotification) {
  if (["submit", "approve-step", "approval-reassign", "remind"].includes(row.action) && row.actionRequired === false) {
    return ["Approved", "HandedOff", "Completed", "InUse", "Issued", "Returned"].includes(row.requestStatus ?? "")
      ? "审批已通过" : row.requestStatus === "Pending" ? "您的审批任务已处理" : "审批任务已结束";
  }
  return notificationActions[row.action] ?? row.action;
}
export function notificationFinanceReceiptStatus(row: SiteNotification) {
  if (row.requestKind !== "oa-expense") return null;
  if (row.requestStatus === "Approved") return { label: "待财务接收", tone: "warning" } as const;
  if (row.requestStatus === "HandedOff") return { label: "已财务接收", tone: "positive" } as const;
  return null;
}
export const communicationAccess = (user: ApiUserDto, resource: "announcements" | "notifications", action = "view") =>
  hasPermission(user.capabilities.permissions, `office.${resource}`, action);
export function announcementActive(row: Announcement, now = Date.now()) {
  return row.status === "Published" && Date.parse(row.startsAt) <= now && Date.parse(row.expiresAt) > now;
}
export function notificationLink(row: Pick<SiteNotification,"requestKind"|"requestId">) {
  if (row.requestKind === "supply-requests") return `/office/supplies?view=requests&requestId=${row.requestId}`;
  if (row.requestKind === "bookings") return `/office/meeting-rooms?view=requests&requestId=${row.requestId}`;
  const kind = row.requestKind.replace(/^oa-/, "");
  return oaKinds.some(value => value === kind) ? `/office/requests/${kind}?requestId=${row.requestId}` : null;
}
