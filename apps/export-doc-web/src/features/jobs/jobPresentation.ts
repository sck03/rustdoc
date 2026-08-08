import type { ApiReportTemplateDto, BackgroundJobSnapshot } from "../../api/index.ts";
import { formatPlainNumber } from "../../ui/formUtils.ts";

export const jobStatusOptions = [
  { value: "Queued", label: "排队中" },
  { value: "Running", label: "运行中" },
  { value: "Succeeded", label: "已完成" },
  { value: "Failed", label: "失败" },
  { value: "Canceling", label: "取消中" },
  { value: "Canceled", label: "已取消" },
];

export function hasActiveJobs(jobs?: BackgroundJobSnapshot[]) {
  return jobs?.some((job) => {
    const status = job.status?.toLowerCase();
    return status === "queued" || status === "running" || status === "canceling";
  }) ?? false;
}

export function formatJobStatus(value?: string) {
  return jobStatusOptions.find((option) => option.value.toLowerCase() === value?.toLowerCase())?.label ?? value ?? "-";
}

export function isTerminalJob(value?: string) {
  const normalized = value?.toLowerCase();
  return normalized === "succeeded" || normalized === "failed" || normalized === "canceled";
}

export function formatProgress(value?: number) {
  return typeof value === "number" ? `${formatPlainNumber(value)}%` : "-";
}

export function readJobMessage(job: BackgroundJobSnapshot) {
  return job.errorMessage || job.detailText || job.statusText || "-";
}

export function formatDateTime(value?: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString("zh-CN", { hour12: false });
}

export function readPathLines(value: string) {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

export function readPositiveIntegerTokens(value: string) {
  const seen = new Set<number>();
  const result: number[] = [];
  for (const token of value.split(/[\s,;，；]+/)) {
    const trimmed = token.trim();
    if (!/^\d+$/.test(trimmed)) continue;
    const parsed = Number.parseInt(trimmed, 10);
    if (parsed > 0 && !seen.has(parsed)) {
      seen.add(parsed);
      result.push(parsed);
    }
  }
  return result;
}

export function findPreferredInvoiceTemplate(templates: ApiReportTemplateDto[]) {
  return templates.find((template) => fileNameFromPath(template.templatePath).toLowerCase() === "invoice_template.html") ?? templates[0];
}

export function fileNameFromPath(value: string) {
  return value.split(/[\\/]/).filter(Boolean).at(-1) ?? value;
}
