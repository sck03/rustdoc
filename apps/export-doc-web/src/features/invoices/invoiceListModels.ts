import type { ApiExcelImportPreviewResponse, ApiInvoiceListItemDto, ApiInvoiceTransferPreviewResponse, SingleWindowExportReview } from "../../api/index.ts";
import type { SingleWindowBusinessType } from "./invoiceListFileNames.ts";

export type InvoiceCopyDraft = { source: ApiInvoiceListItemDto; newInvoiceNo: string; copyHeader: boolean; copyItems: boolean; resetStatus: boolean; resetDates: boolean; clearAmounts: boolean };

export type InvoiceTransferConflictAction = "Skip" | "Overwrite" | "NewInvoiceNo" | "AppendItems";
export type InvoiceTransferImportDraft = {
  packagePath: string;
  previewResponse: ApiInvoiceTransferPreviewResponse | null;
  conflictAction: InvoiceTransferConflictAction;
  newInvoiceNo: string;
  allowInvalidChecksum: boolean;
};

export function buildDefaultCopyInvoiceNo(invoiceNo: string) {
  const normalized = invoiceNo.trim();
  return normalized ? `${normalized}副本` : "新发票副本";
}

export function validateInvoiceCopyDraft(draft: InvoiceCopyDraft) {
  return draft.newInvoiceNo.trim() ? null : "新发票号不能为空。";
}

export function normalizeRequiredPackagePath(packagePath: string) {
  const value = packagePath.trim();
  return value ? { value, error: null } : { value: "", error: "单据包路径不能为空。" };
}

export function validateInvoiceTransferImportDraft(draft: InvoiceTransferImportDraft) {
  if (!draft.packagePath.trim()) return "单据包路径不能为空。";
  if (!draft.previewResponse) return "请先预览单据包。";
  return null;
}

export function matchesSingleWindowReview(invoiceId: number, businessType: SingleWindowBusinessType, reviewInvoiceId: number | null, reviewBusinessType: SingleWindowBusinessType | null, review: SingleWindowExportReview | null) {
  return Boolean(review && reviewInvoiceId === invoiceId && reviewBusinessType === businessType);
}

export function buildExcelImportRouteSuccessMessage(response: ApiExcelImportPreviewResponse) {
  if (!response.success && response.errors?.length) {
    return `Excel 已生成发票草稿，但有 ${response.errors.length} 个问题：${response.errors.slice(0, 2).join("；")}。`;
  }

  const analysis = response.analysisReport;
  if (!analysis) return "已从 Excel 生成发票草稿，请核对后保存。";
  const analyzerLabel = analysis.analyzerId === "rust-calamine" ? "Rust" : analysis.analyzerId || "智能";
  const confidence = Number.isFinite(analysis.confidence) ? `${Math.round(Math.min(1, Math.max(0, analysis.confidence)) * 100)}%` : "0%";
  const detected = [
    response.invoice?.invoiceNo ? "发票号" : "",
    response.invoice?.exporterNameEN ? "SHIPPER" : "",
    response.invoice?.customerNameEN ? "收货人" : "",
    response.invoice?.portOfLoading ? "起运港" : "",
    response.invoice?.portOfDestination ? "目的地" : "",
  ].filter(Boolean);
  return `${analyzerLabel} Excel 分析 ${confidence}：${analysis.selectedWorksheetName || "工作表"}，${detected.length ? `识别 ${detected.join("、")}` : "字段需复核"}，已生成发票草稿。`;
}

export function createEmptyInvoiceTransferImportDraft(): InvoiceTransferImportDraft {
  return { packagePath: "", previewResponse: null, conflictAction: "NewInvoiceNo", newInvoiceNo: "", allowInvalidChecksum: false };
}

export function createInvoiceTransferImportDraft(packagePath: string, previewResponse: ApiInvoiceTransferPreviewResponse): InvoiceTransferImportDraft {
  const preview = previewResponse.preview;
  const conflictAction: InvoiceTransferConflictAction = preview.invoiceExists && preview.invoiceMatches ? "Skip" : "NewInvoiceNo";
  const newInvoiceNo = preview.invoiceExists && preview.invoiceNo.trim() ? `${preview.invoiceNo.trim()}_IMPORTED` : preview.invoiceNo.trim();
  return { packagePath, previewResponse, conflictAction, newInvoiceNo, allowInvalidChecksum: false };
}

export function buildSingleWindowReviewMessage(review: SingleWindowExportReview, businessType: SingleWindowBusinessType) {
  const label = formatSingleWindowBusinessType(businessType);
  if (!review.hasIssues && review.totalErrorCount === 0 && review.totalWarningCount === 0) return `${label} 导出前预检未发现问题，已进入导出步骤。`;
  const repairCount = getAutoRepairGroupKeys(review).length;
  const repairHint = repairCount > 0 ? `可自动修复 ${repairCount} 个分组。` : "没有可自动修复分组。";
  return `${label} 导出前预检完成：错误 ${review.totalErrorCount}，警告 ${review.totalWarningCount}。${repairHint} 确认后可再次点击导出继续。`;
}

export function flattenSingleWindowReviewIssues(review: SingleWindowExportReview | null) {
  return review?.groups.flatMap((group) => group.issues.map((issue) => ({ group, issue }))) ?? [];
}

export function getAutoRepairGroupKeys(review: SingleWindowExportReview) {
  return review.groups.filter((group) => group.canAutoRepair || group.issues.some((issue) => issue.canAutoRepair)).map((group) => group.groupKey).filter((key) => key.trim().length > 0);
}

export function formatSingleWindowBusinessType(value: SingleWindowBusinessType | null) {
  return value === "CustomsCoo" ? "COO" : value === "AgentConsignment" ? "ACD" : "单一窗口";
}

export function formatSingleWindowNavigationTarget(target?: { goodsLineNo: number; groupKey: string; propertyKey: string }) {
  if (!target) return "-";
  const field = target.propertyKey || target.groupKey;
  return target.goodsLineNo > 0 ? `第 ${target.goodsLineNo} 行 · ${field}` : field || "-";
}

export function formatReviewSeverity(severity: number) {
  return severity >= 2 ? "错误" : severity === 1 ? "警告" : "提示";
}

export function formatReviewSeverityKey(severity: number) {
  return severity >= 2 ? "error" : severity === 1 ? "warning" : "info";
}
