import type { QueryClient } from "@tanstack/react-query";
import type { ApiSingleWindowClientProfileDto,SingleWindowOperationCenterDetail,SingleWindowWorkstationRow } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readStoredJsonObject,writeStoredJson } from "../../ui/browserStorage.ts";
import { normalizeListPageSize } from "../../ui/listViewState.ts";

import { formatPlainNumber } from "../../ui/formUtils.ts";

export const singleWindowOperationCenterViewStateStorageKey = "export-doc-manager.single-window.operation-center.list-view-state.v1";
export const singleWindowCollaborationViewStateStorageKey = "export-doc-manager.single-window.collaboration.list-view-state.v1";

export const businessTypeOptions = [
  { value: "CustomsCoo", label: "海关原产地证" },
  { value: "AgentConsignment", label: "报关代理委托" },
];

export const batchStatusOptions = [
  { value: "SubmitPackageExported", label: "已导出提交包" },
  { value: "SubmitPackageImported", label: "已导入提交包" },
  { value: "QueuedToClient", label: "已送入导入目录" },
  { value: "ReceiptPackageExported", label: "已导出回执包" },
  { value: "ReceiptImported", label: "已导入回执包" },
  { value: "Received", label: "已接收" },
  { value: "Accepted", label: "已受理" },
  { value: "PendingReview", label: "待审核" },
  { value: "Approved", label: "已通过" },
  { value: "Rejected", label: "已退回" },
  { value: "Failed", label: "失败" },
];

export const collaborationStatusOptions = [
  { value: "Pending", label: "待处理" },
  { value: "Assigned", label: "已指派" },
  { value: "Submitted", label: "已提交" },
  { value: "Completed", label: "已完成" },
  { value: "Failed", label: "失败" },
];

export type SingleWindowOperationCenterViewState = {
  keyword: string;
  businessType: string;
  status: string;
  pageSize: number;
};

export type SingleWindowCollaborationViewState = SingleWindowOperationCenterViewState & {
  includeDisabledWorkstations: boolean;
};

export function loadSingleWindowOperationCenterViewState(): SingleWindowOperationCenterViewState {
  const parsed = readStoredViewState(singleWindowOperationCenterViewStateStorageKey);
  return {
    keyword: readStoredString(parsed.keyword),
    businessType: readStoredString(parsed.businessType),
    status: readStoredString(parsed.status),
    pageSize: normalizeListPageSize(parsed.pageSize),
  };
}

export function saveSingleWindowOperationCenterViewState(state: SingleWindowOperationCenterViewState) {
  writeStoredViewState(singleWindowOperationCenterViewStateStorageKey, {
    keyword: state.keyword.trim(),
    businessType: state.businessType,
    status: state.status,
    pageSize: normalizeListPageSize(state.pageSize),
  });
}

export function loadSingleWindowCollaborationViewState(): SingleWindowCollaborationViewState {
  const parsed = readStoredViewState(singleWindowCollaborationViewStateStorageKey);
  return {
    keyword: readStoredString(parsed.keyword),
    businessType: readStoredString(parsed.businessType),
    status: readStoredString(parsed.status),
    includeDisabledWorkstations: parsed.includeDisabledWorkstations === true,
    pageSize: normalizeListPageSize(parsed.pageSize),
  };
}

export function saveSingleWindowCollaborationViewState(state: SingleWindowCollaborationViewState) {
  writeStoredViewState(singleWindowCollaborationViewStateStorageKey, {
    keyword: state.keyword.trim(),
    businessType: state.businessType,
    status: state.status,
    includeDisabledWorkstations: state.includeDisabledWorkstations,
    pageSize: normalizeListPageSize(state.pageSize),
  });
}

export function readStoredViewState(storageKey: string): Record<string, unknown> {
  return readStoredJsonObject(storageKey);
}

export function writeStoredViewState(storageKey: string, state: Record<string, unknown>) {
  writeStoredJson(storageKey, state);
}

export function readStoredString(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}


export function formatWorkstationCapabilities(workstation: SingleWindowWorkstationRow) {
  const capabilities = [];
  if (workstation.canSubmitCustomsCoo) {
    capabilities.push("海关原产地证");
  }

  if (workstation.canSubmitAgentConsignment) {
    capabilities.push("代理委托");
  }

  return capabilities.length > 0 ? capabilities.join("、") : "-";
}

export function formatBusinessType(value?: string) {
  return formatOptionLabel(value, businessTypeOptions);
}

export function formatBatchStatus(value?: string) {
  return formatOptionLabel(value, batchStatusOptions);
}

export function formatCollaborationStatus(value?: string) {
  return formatOptionLabel(value, collaborationStatusOptions);
}

export function formatReceiptStatus(value?: string) {
  const receiptStatusOptions = [
    { value: "Received", label: "已接收" },
    { value: "Accepted", label: "已受理" },
    { value: "Rejected", label: "已退回" },
    { value: "PendingReview", label: "待审核" },
    { value: "Approved", label: "已通过" },
    { value: "Failed", label: "失败" },
  ];

  return formatOptionLabel(value, receiptStatusOptions);
}

export function formatReceiptKind(value?: string) {
  const receiptKindOptions = [
    { value: "CustomsCooBusinessReceipt", label: "海关原产地证业务回执" },
    { value: "CustomsCooTechnicalReceipt", label: "海关原产地证技术回执" },
    { value: "CustomsCooAttachmentReceipt", label: "海关原产地证附件回执" },
    { value: "AgentConsignmentImportResponse", label: "代理委托导入响应" },
    { value: "AgentConsignmentAcd002", label: "代理委托协议回执" },
  ];

  return formatOptionLabel(value, receiptKindOptions);
}

export function formatParsedBusinessType(value?: number) {
  const businessTypeByValue = new Map([
    [0, "海关原产地证"],
    [1, "报关代理委托"],
  ]);

  return typeof value === "number" ? businessTypeByValue.get(value) ?? formatPlainNumber(value) : "-";
}

export function formatPackageType(value?: number) {
  const packageTypeByValue = new Map([
    [0, "提交包"],
    [1, "回执包"],
  ]);

  return typeof value === "number" ? packageTypeByValue.get(value) ?? formatPlainNumber(value) : "-";
}

export function formatParsedReceiptKind(value?: number) {
  const receiptKindByValue = new Map([
    [1, "海关原产地证业务回执"],
    [2, "海关原产地证技术回执"],
    [3, "海关原产地证附件回执"],
    [4, "代理委托导入响应"],
    [5, "代理委托协议回执"],
  ]);

  return typeof value === "number" ? receiptKindByValue.get(value) ?? formatPlainNumber(value) : "-";
}

export function formatParsedReceiptStatus(value?: number) {
  const receiptStatusByValue = new Map([
    [1, "已接收"],
    [2, "已受理"],
    [3, "已退回"],
    [4, "待审核"],
    [5, "已通过"],
    [6, "失败"],
  ]);

  return typeof value === "number" ? receiptStatusByValue.get(value) ?? formatPlainNumber(value) : "-";
}

export function formatOptionLabel(value: string | undefined, options: Array<{ value: string; label: string }>) {
  if (!value) {
    return "-";
  }

  return options.find((option) => option.value.toLowerCase() === value.toLowerCase())?.label ?? value;
}

export function formatDateTime(value?: string) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString("zh-CN", {
        hour12: false,
      });
}

export function readDisplayText(value?: string) {
  return value?.trim() ? value : "-";
}

export function readDisplayValue(value?: string | number) {
  if (typeof value === "number") {
    return formatPlainNumber(value);
  }

  return readDisplayText(value);
}

export function resolveBusinessClientRoot(
  profile: ApiSingleWindowClientProfileDto,
  businessType: string,
  kind: "import" | "receipt",
) {
  const override = readBusinessDirectoryOverride(profile.businessDirectoryOverridesJson, businessType);
  const overridePath = kind === "import" ? override?.importRootPath : override?.receiptRootPath;
  if (overridePath?.trim()) {
    return overridePath.trim();
  }

  const globalPath = kind === "import" ? profile.importRootPath : profile.receiptRootPath;
  return globalPath?.trim() ?? "";
}

export function readBusinessDirectoryOverride(json: string | undefined, businessType: string) {
  if (!json?.trim() || !businessType.trim()) {
    return null;
  }

  try {
    const parsed = JSON.parse(json) as {
      businesses?: SingleWindowBusinessDirectoryOverride[];
      Businesses?: SingleWindowBusinessDirectoryOverride[];
    };
    const businesses = parsed.businesses ?? parsed.Businesses ?? [];
    const normalizedBusinessType = businessType.trim().toLowerCase();
    const matched = businesses.find((item) => readOverrideString(item, "businessType").toLowerCase() === normalizedBusinessType);
    if (!matched) {
      return null;
    }

    return {
      businessType: readOverrideString(matched, "businessType"),
      importRootPath: readOverrideString(matched, "importRootPath"),
      receiptRootPath: readOverrideString(matched, "receiptRootPath"),
    };
  } catch {
    return null;
  }
}

export type SingleWindowBusinessDirectoryOverride = {
  businessType?: string;
  importRootPath?: string;
  receiptRootPath?: string;
  BusinessType?: string;
  ImportRootPath?: string;
  ReceiptRootPath?: string;
};

export function readOverrideString(
  item: SingleWindowBusinessDirectoryOverride,
  key: "businessType" | "importRootPath" | "receiptRootPath",
) {
  const pascalKey: keyof SingleWindowBusinessDirectoryOverride =
    key === "businessType" ? "BusinessType" : key === "importRootPath" ? "ImportRootPath" : "ReceiptRootPath";
  const value = item[key] ?? item[pascalKey];
  return typeof value === "string" ? value.trim() : "";
}

export function buildClientBoxPath(rootPath: string, boxName: "OutBox" | "SentBox" | "InBox" | "FailBox") {
  const normalizedRoot = rootPath.trim().replace(/[\\/]+$/, "");
  if (!normalizedRoot) {
    return "";
  }

  const separator = normalizedRoot.includes("/") && !normalizedRoot.includes("\\") ? "/" : "\\";
  return `${normalizedRoot}${separator}${boxName}`;
}

export function buildReceiptPackageFileName(detail: Pick<SingleWindowOperationCenterDetail, "batchId" | "batchReference" | "invoiceNo">) {
  const fileName = toSafeFileName(detail.batchReference || detail.invoiceNo || `batch-${detail.batchId}`);
  return `${fileName || "receipt-package"}.swpkg`;
}

export async function invalidateSingleWindowBatchQueries(
  queryClient: QueryClient,
  batchId: number,
) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
    queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterDetail(batchId) }),
  ]);
}

export function toSafeFileName(value: string) {
  return value.replace(/[<>:"/\\|?*\x00-\x1F]/g, "_").trim();
}

export function mergePathLines(value: string, additions: string[]) {
  const seen = new Set<string>();
  const paths: string[] = [];
  for (const path of [...parseReceiptFilePaths(value), ...additions]) {
    const key = path.toLowerCase();
    if (!seen.has(key)) {
      paths.push(path);
      seen.add(key);
    }
  }

  return paths.join("\n");
}

export function parseReceiptFilePaths(value: string) {
  const seen = new Set<string>();
  const paths: string[] = [];
  for (const line of value.split(/\r?\n/)) {
    const trimmed = line.trim();
    const key = trimmed.toLowerCase();
    if (trimmed && !seen.has(key)) {
      paths.push(trimmed);
      seen.add(key);
    }
  }

  return paths;
}
