import type { ApiUserDto, MeetingBookingRecord, MeetingBookingUpdateRequest, MeetingRoomSaveRequest, OfficeSupplyRequestRecord, OfficeSupplySaveRequest, SupplyRequestUpdateRequest } from "../../api/index.ts";
import { businessDateTimeLocalInputToIso, toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";

export type OfficeKind = "rooms" | "supplies";
export type OfficeRequestRow = MeetingBookingRecord | OfficeSupplyRequestRecord;
export type OfficeAction = "approve" | "reject" | "cancel" | "issue" | "return";
export function officeRequestFocus(search: URLSearchParams) {
  const id = (key: string) => {
    const text = search.get(key) ?? "";
    const number = Number(text);
    return /^[1-9]\d*$/.test(text) && Number.isSafeInteger(number) && number <= 2147483647 ? number : undefined;
  };
  return { requestId: id("requestId"), applicantUserId: id("applicantUserId"), employeeId: id("employeeId") };
}
export type OfficePage<T> = { items: T[]; totalCount: number; totalPages: number; pageNumber: number; pageSize: number };

export const officeStatusLabels: Record<string, string> = {
  Pending: "待审批", Approved: "待领用", InUse: "使用中", Completed: "已完成",
  Rejected: "已驳回", Cancelled: "已取消", Issued: "已领用", Returned: "已归还",
};
export const officeActionLabels = { approve: "批准", reject: "驳回", cancel: "取消申请", issue: "确认发放", return: "登记归还" };
export const officeHistoryLabels: Record<string, string> = {
  Submit: "提交申请", Register: "登记记录", Approve: "审批通过", Reject: "驳回申请", Cancel: "取消申请",
  Issue: "发放／交接", Return: "归还登记", Restock: "补充入库", Stocktake: "盘点调整",
  Edit: "修改记录",
};

export function officeAccess(user: ApiUserDto, kind: OfficeKind) {
  const grants = (user.capabilities.permissions ?? []).filter((grant) => grant.resourceKey === `office.${kind}`);
  const scope = (action: string) => grants.find((grant) => grant.action === action)?.dataScope ?? "";
  return {
    canSeeOthers: ["department", "company", "all"].includes(scope("view")),
    allows: (action: string, row?: OfficeRequestRow) => {
      const value = scope(action);
      if (!["own", "department", "company", "all"].includes(value)) return false;
      if (!row) return true;
      if (value === "own") return row.ownerUserId === user.id;
      if (value === "department") return Boolean(user.departmentId) && row.departmentId === user.departmentId;
      return true;
    },
  };
}

export function isMeetingBooking(row: OfficeRequestRow): row is MeetingBookingRecord { return "meetingRoomId" in row; }

export function readMeetingBookingUpdate(form: FormData, version: number, timeZone: string): MeetingBookingUpdateRequest {
  const startsAt = businessDateTimeLocalInputToIso(String(form.get("startsAt") ?? ""), timeZone);
  const endsAt = businessDateTimeLocalInputToIso(String(form.get("endsAt") ?? ""), timeZone);
  if (!startsAt || !endsAt) throw new Error("请选择有效的预约起止时间。");
  return { expectedVersion: version, title: String(form.get("title") ?? "").trim(), attendeeCount: Number(form.get("attendeeCount")), startsAt, endsAt };
}

export function readSupplyRequestUpdate(form: FormData, version: number): SupplyRequestUpdateRequest {
  return { expectedVersion: version, quantity: Number(form.get("quantity")), purpose: String(form.get("purpose") ?? "").trim(),
    returnDueDate: String(form.get("returnDueDate") ?? "") || null };
}

export function officeRequestActions(row: OfficeRequestRow, user: ApiUserDto, kind: OfficeKind) {
  const access = officeAccess(user, kind);
  const actions: { action: OfficeAction; label: string }[] = [];
  if (row.status === "Pending" && row.ownerUserId !== user.id && access.allows("approve", row)) {
    actions.push({ action: "approve", label: "批准" }, { action: "reject", label: "驳回" });
  }
  if ((row.status === "Pending" || row.status === "Approved") && access.allows("cancel", row))
    actions.push({ action: "cancel", label: user.capabilities.usesOfficeRegister ? "取消登记" : "取消申请" });
  if (row.status === "Approved" && access.allows("issue", row))
    actions.push({ action: "issue", label: isMeetingBooking(row) ? (row.requiresKey ? "发放钥匙" : "登记使用") : "确认发放" });
  if ((row.status === "InUse" || row.status === "Issued" && !isMeetingBooking(row) && row.isReturnable) && access.allows("return", row))
    actions.push({ action: "return", label: isMeetingBooking(row) ? (row.requiresKey ? "归还钥匙" : "结束使用") : "登记归还" });
  return actions;
}

export function officeStatus(row: OfficeRequestRow, businessDate: string, now = Date.now()) {
  if (isMeetingBooking(row)) {
    if (row.status === "InUse" && Date.parse(row.endsAt) <= now) return "超时未归还";
    if (["Pending", "Approved"].includes(row.status) && Date.parse(row.endsAt) <= now) return "已过期（未使用）";
    if (row.status === "Approved") return row.requiresKey ? "待领钥匙" : "待使用";
  } else if (row.status === "Issued" && row.isReturnable) {
    if (row.returnDueDate && row.returnDueDate < businessDate) return "逾期未归还";
    return row.returnedQuantity > 0 ? "部分归还" : "借用中";
  }
  return officeStatusLabels[row.status] ?? row.status;
}

export function officeBookingDefaults(timeZone: string, now = Date.now()) {
  const start = Math.ceil((now + 15 * 60000) / (15 * 60000)) * (15 * 60000);
  return { start: toBusinessDateTimeLocalInput(new Date(start).toISOString(), timeZone),
    end: toBusinessDateTimeLocalInput(new Date(start + 3600000).toISOString(), timeZone) };
}

export function officeDayRange(day: string, timeZone: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return null;
  try {
    const next = new Date(`${day}T12:00:00Z`);
    next.setUTCDate(next.getUTCDate() + 1);
    const from = businessDateTimeLocalInputToIso(`${day}T00:00`, timeZone);
    const to = businessDateTimeLocalInputToIso(`${next.toISOString().slice(0, 10)}T00:00`, timeZone);
    return from && to ? { from, to } : null;
  } catch { return null; }
}

export function shiftOfficeBookingEnd(start: string, timeZone: string) {
  try {
    const instant = businessDateTimeLocalInputToIso(start, timeZone);
    return instant ? toBusinessDateTimeLocalInput(new Date(Date.parse(instant) + 3600000).toISOString(), timeZone) : "";
  } catch { return ""; }
}

export function readMeetingRoomForm(form: FormData, version: number): MeetingRoomSaveRequest {
  return { name: String(form.get("name") ?? ""), location: String(form.get("location") ?? ""), equipment: String(form.get("equipment") ?? ""),
    capacity: Number(form.get("capacity")), maximumBookingHours: Number(form.get("maximumBookingHours")),
    advanceBookingDays: Number(form.get("advanceBookingDays")), requiresKey: form.has("requiresKey"), isActive: form.has("isActive"), expectedVersion: version };
}

export function readOfficeSupplyForm(form: FormData, version: number): OfficeSupplySaveRequest {
  return { name: String(form.get("name") ?? ""), unit: String(form.get("unit") ?? ""), location: String(form.get("location") ?? ""),
    description: String(form.get("description") ?? ""), isReturnable: form.has("isReturnable"), isActive: form.has("isActive"),
    minimumStock: Number(form.get("minimumStock")), expectedVersion: version };
}
