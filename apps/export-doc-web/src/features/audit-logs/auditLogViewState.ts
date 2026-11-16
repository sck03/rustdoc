import { readStoredJsonObject, writeStoredJson } from "../../ui/browserStorage.ts";
import { normalizeListPageSize } from "../../ui/listViewState.ts";
import { type AuditLogFilters } from "./auditLogMaintenanceModel.ts";
export const entityLabels: Record<string, string> = {
  Invoice: "发票",
  Customer: "单证客户",
  Exporter: "出口商",
  Payment: "付款报销",
  Payee: "收款对象",
  Product: "商品资料",
  HsCode: "HS 编码",
  User: "系统账号",
  PermissionTemplate: "权限方案",
  UserReportTemplate: "用户报表模板",
  UserReportTemplateVersion: "模板版本",
  Personnel: "人员档案",
  MeetingBooking: "会议室预约",
  OfficeSupplyRequest: "物品领用",
  CrmCustomer: "CRM 客户",
  CrmContact: "客户联系人",
  CrmFollowUp: "客户跟进",
  SalesOpportunity: "商机与报价",
  SalesOpportunityHistory: "商机版本历史",
  SupplierCompany: "供应商",
  SupplierContact: "供应商联系人",
  SupplierProductLink: "供应产品",
  SupplierAssessment: "供应商评价",
  BusinessAttachment: "业务资料",
  BusinessAttachmentCategory: "资料分类",
};

export const actionLabels: Record<string, string> = { Added: "新增", Modified: "修改", Deleted: "删除" };

export function loadAuditLogViewState(): AuditLogViewState {
  const parsed = readStoredJsonObject(auditLogViewStateStorageKey);
  return {
    invoiceKeyword: readStoredString(parsed.invoiceKeyword),
    entityName: readStoredOption(parsed.entityName, entityOptions),
    action: readStoredOption(parsed.action, actionOptions),
    userId: readStoredString(parsed.userId),
    keyword: readStoredString(parsed.keyword),
    enableDateRange: parsed.enableDateRange === true,
    startTime: readStoredString(parsed.startTime) || readDateTimeLocalValue(addDays(new Date(), -7)),
    endTime: readStoredString(parsed.endTime) || readDateTimeLocalValue(new Date()),
    retentionDays: readPositiveIntegerText(parsed.retentionDays, "180"),
    pageSize: normalizeListPageSize(parsed.pageSize),
  };
}

export function saveAuditLogViewState(state: AuditLogViewState) {
  writeStoredJson(auditLogViewStateStorageKey, {
    invoiceKeyword: state.invoiceKeyword.trim(),
    entityName: state.entityName,
    action: state.action,
    userId: state.userId.trim(),
    keyword: state.keyword.trim(),
    enableDateRange: state.enableDateRange,
    startTime: state.startTime,
    endTime: state.endTime,
    retentionDays: readPositiveIntegerText(state.retentionDays, "180"),
    pageSize: normalizeListPageSize(state.pageSize),
  });
}

export function createCommittedAuditLogFilters(state: AuditLogViewState): AuditLogFilters {
  return {
    invoiceKeyword: state.invoiceKeyword.trim(),
    entityName: state.entityName,
    action: state.action,
    userId: state.userId.trim(),
    keyword: state.keyword.trim(),
    startTime: state.enableDateRange ? readIsoDateTime(state.startTime) : "",
    endTime: state.enableDateRange ? readIsoDateTime(state.endTime) : "",
  };
}

function readStoredString(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function readStoredOption(value: unknown, options: readonly string[]) {
  const text = readStoredString(value);
  return options.includes(text) ? text : "";
}

function readPositiveIntegerText(value: unknown, fallback: string) {
  const numericValue = Number(value);
  if (!Number.isFinite(numericValue) || numericValue <= 0) {
    return fallback;
  }

  return String(Math.max(1, Math.trunc(numericValue)));
}

function readIsoDateTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : date.toISOString();
}

function readDateTimeLocalValue(value: Date) {
  const pad = (number: number) => String(number).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}T${pad(value.getHours())}:${pad(value.getMinutes())}`;
}

function addDays(value: Date, days: number) {
  const next = new Date(value);
  next.setDate(next.getDate() + days);
  return next;
}

export function createAuditLogFilterRequest(filters: AuditLogFilters) {
  return {
    invoiceKeyword: filters.invoiceKeyword || undefined,
    entityName: filters.entityName || undefined,
    action: filters.action || undefined,
    userId: filters.userId || undefined,
    keyword: filters.keyword || undefined,
    startTime: filters.startTime || undefined,
    endTime: filters.endTime || undefined,
  };
}

export function buildAuditLogExportFileName() {
  const now = new Date();
  const pad = (number: number) => String(number).padStart(2, "0");
  return `AuditLogs_${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}.xlsx`;
}

export const entityOptions = ["", ...Object.keys(entityLabels)];

export const actionOptions = ["", "Added", "Modified", "Deleted"];

const auditLogViewStateStorageKey = "export-doc-manager.audit-log.list-view-state.v1";

export type AuditLogViewState = {
  invoiceKeyword: string;
  entityName: string;
  action: string;
  userId: string;
  keyword: string;
  enableDateRange: boolean;
  startTime: string;
  endTime: string;
  retentionDays: string;
  pageSize: number;
};
