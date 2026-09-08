import type { WorklistDueFilter, WorklistItem } from "../../api/index.ts";

export const worklistDueOptions: ReadonlyArray<{ value: WorklistDueFilter; label: string }> = [
  { value: "All", label: "全部待办" }, { value: "Overdue", label: "已到期" },
  { value: "Upcoming", label: "未来 30 天" }, { value: "Undated", label: "未设截止日期" },
];

export function worklistTarget(item: WorklistItem) {
  switch (item.source) {
    case "invoice-review": return `/invoices/${item.recordId}`;
    case "customer-follow-up": return `/crm/follow-ups?followUpId=${item.recordId}`;
    case "meeting-approval": case "meeting-collection": case "meeting-return":
      return `/office/meeting-rooms?view=requests&requestId=${item.recordId}`;
    case "supply-approval": case "supply-collection": case "supply-return":
      return `/office/supplies?view=requests&requestId=${item.recordId}`;
    case "probation-end": case "contract-end": return `/office/people?employeeId=${item.recordId}`;
    default: return null;
  }
}

export function worklistDueLabel(item: WorklistItem, timeZone: string) {
  if (item.dueDate) return item.dueDate;
  if (item.dueAt) return new Intl.DateTimeFormat("zh-CN", { timeZone, dateStyle: "short", timeStyle: "short" }).format(new Date(item.dueAt));
  return "未设截止日期";
}
