import { ExactDecimal } from "./decimalSum.ts";
import { ApiError, type ApiErrorResponse } from "../api/index.ts";

function readErrorResponse(error: ApiError): (Partial<ApiErrorResponse> & { error?: string }) | null {
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
    if (!parsed) return error.responseText || error.message;
    return (typeof parsed.message === "string" && parsed.message)
      || (typeof parsed.error === "string" && parsed.error)
      || error.message;
  }

  return error instanceof Error ? error.message : "请求失败";
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
    if (!/^\d+(?:\.\d+)?$/.test(value) || value.length > 60) return "-";
    const [whole, fraction] = ExactDecimal.from(value).toFixed(2).split(".");
    return `${currency} ${BigInt(whole).toLocaleString("zh-CN")}.${fraction}`.trim();
  }
  if (!Number.isFinite(value)) {
    return "-";
  }

  return `${currency || ""} ${Number(value).toLocaleString("zh-CN", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`.trim();
}

export function formatPlainNumber(value?: number) {
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
