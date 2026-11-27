import { formatDecimal } from "./decimalSum.ts";
import { ApiError, type ApiErrorResponse } from "../api/index.ts";

export function readErrorResponse(error: ApiError): Partial<ApiErrorResponse> | null {
  try {
    const value: unknown = JSON.parse(error.responseText);
    return value !== null && typeof value === "object" && !Array.isArray(value) ? value : {};
  } catch {
    return null;
  }
}

export function readApiError(error: unknown): string {
  if (error instanceof ApiError) {
    const parsed = readErrorResponse(error);
    if (!parsed || typeof parsed.message !== "string" || !parsed.message) {
      return `服务器未返回可识别的错误说明（HTTP ${error.status}）。请检查服务或网关状态；写入结果不明时先查询处理结果。`;
    }
    return formatErrorResponse(parsed, error.status >= 500);
  }

  if (error instanceof TypeError) return "无法连接服务，请检查网络和服务状态；写入结果不明时先查询处理结果。";
  return error instanceof Error ? error.message : "请求失败，未取得可识别的错误详情。";
}
export function formatErrorResponse(parsed: Partial<ApiErrorResponse>, includeTrace = true): string {
  const suggestions = Array.isArray(parsed.issues)
    ? [...new Set(parsed.issues.slice(0,32).map(issue=>issue?.suggestion).filter((value):value is string=>typeof value==="string"&&!!value))] : [];
  const receipt = parsed.outcome === "Unknown" ? "结果尚未确认，请先查询处理回执，避免重复提交。" : "";
  const trace = includeTrace && typeof parsed.requestId === "string" && parsed.requestId ? `追踪编号：${parsed.requestId}` : "";
  return [parsed.message,...suggestions,receipt,trace].filter(Boolean).join(" ");
}

export function isConcurrencyConflict(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409 && readErrorResponse(error)?.code === "ConcurrencyConflict";
}

export function readRouteSuccessMessage(state: unknown) {
  if (state && typeof state === "object" && "successMessage" in state) {
    const value = (state as { successMessage?: unknown }).successMessage;
    return typeof value === "string" ? value : null;
  }

  return null;
}

export function toDateInputValue(value?: string | null) {
  const normalized = value?.trim() ?? "";
  return /^\d{4}-\d{2}-\d{2}$/.test(normalized) ? normalized : "";
}

export function dateInputToApiDate(value: string) {
  return toDateInputValue(value);
}

export function currentLocalDateInputValue(value = new Date()) {
  if (Number.isNaN(value.getTime())) {
    return "";
  }

  const year = String(value.getFullYear()).padStart(4, "0");
  const month = String(value.getMonth() + 1).padStart(2, "0");
  const day = String(value.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function readNumber(value: string) {
  const next = Number(value);
  return Number.isFinite(next) ? next : 0;
}

export function numberInputValue(value?: number) {
  return Number.isFinite(value) ? String(value) : "0";
}

export function formatDate(value?: string | null) {
  if (!value) {
    return "-";
  }

  const dateOnly = toDateInputValue(value);
  if (dateOnly) {
    const [year, month, day] = dateOnly.split("-");
    return `${Number(year)}/${Number(month)}/${Number(day)}`;
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "-" : date.toLocaleDateString("zh-CN");
}

export function formatAmount(value?: number | string, currency = "") {
  if (typeof value === "string") {
    if (!/^[+-]?\d+(?:\.\d+)?$/.test(value) || value.length > 60) return "-";
    return `${currency} ${formatDecimal(value, 2)}`.trim();
  }
  if (!Number.isFinite(value)) {
    return "-";
  }

  return `${currency || ""} ${Number(value).toLocaleString("zh-CN", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`.trim();
}

export function formatPlainNumber(value?: number | string) {
  if (typeof value === "string") return formatDecimal(value, 4, true);
  if (!Number.isFinite(value)) {
    return "-";
  }

  return Number(value).toLocaleString("zh-CN", {
    maximumFractionDigits: 4,
  });
}

export function normalizeText(value?: string | null) {
  return value?.trim() ?? "";
}

export function numberValue(value?: number | null) {
  return Number.isFinite(value) ? Number(value) : 0;
}
