import type { Announcement, ApiUserDto, SiteNotification } from "../../api/index.ts";
import { hasPermission } from "../../app/PermissionAccessContext.tsx";
import { oaKinds } from "../oa/oaModel.ts";

export const announcementStatus = { Draft: "草稿", Published: "已发布", Withdrawn: "已撤回", Archived: "已归档" };
export const notificationActions: Record<string, string> = { submit: "待您审批", approve: "审批通过", reject: "审批驳回", complete: "已办结 / 移交", void: "批准已作废" };
export const communicationAccess = (user: ApiUserDto, resource: "announcements" | "notifications", action = "view") =>
  hasPermission(user.capabilities.permissions, `office.${resource}`, action);
export function announcementActive(row: Announcement, now = Date.now()) {
  return row.status === "Published" && Date.parse(row.startsAt) <= now && Date.parse(row.expiresAt) > now;
}
export function notificationLink(row: SiteNotification) {
  const kind = row.requestKind.replace(/^oa-/, "");
  return oaKinds.some(value => value === kind) ? `/office/requests/${kind}?requestId=${row.requestId}` : null;
}
