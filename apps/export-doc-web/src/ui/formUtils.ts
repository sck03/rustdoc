import { ApiError } from "../api/index.ts";

export function readApiError(error: unknown): string {
  if (error instanceof ApiError) {
    try {
      const parsed = JSON.parse(error.responseText) as { message?: string; error?: string };
      return parsed.message || parsed.error || error.message;
    } catch {
      return error.responseText || error.message;
    }
  }

  return error instanceof Error ? error.message : "请求失败";
}

export function isConcurrencyConflict(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409 && /其他用户|其他会话|并发版本|加载最新|刷新后重试/.test(readApiError(error));
}

export function readRouteSuccessMessage(state: unknown) {
  if (state && typeof state === "object" && "successMessage" in state) {
    const value = (state as { successMessage?: unknown }).successMessage;
    return typeof value === "string" ? value : null;
  }

  return null;
}

export function toDateInputValue(value?: string) {
  if (!value) {
    return "";
  }

  const parsed = new Date(value);
  if (!Number.isNaN(parsed.getTime())) {
    return parsed.toISOString().slice(0, 10);
  }

  return value.slice(0, 10);
}

export function dateInputToApiDate(value: string) {
  const date = value || new Date().toISOString().slice(0, 10);
  return `${date}T00:00:00`;
}

export function readNumber(value: string) {
  const next = Number(value);
  return Number.isFinite(next) ? next : 0;
}

export function numberInputValue(value?: number) {
  return Number.isFinite(value) ? String(value) : "0";
}

export function formatDate(value?: string) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString("zh-CN");
}

export function formatAmount(value?: number, currency = "") {
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

export function normalizeText(value?: string) {
  return value?.trim() ?? "";
}

export function numberValue(value?: number) {
  return Number.isFinite(value) ? Number(value) : 0;
}
