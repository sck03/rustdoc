export function formatRuntimeDate(value?: string) {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

export function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return "0 B";
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
}

export function formatSettingDateTime(value: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

export const serverManagedDirectoryLabel = "服务器受管目录";
export const serverManagedFileLabel = "服务器受管文件";

export function formatManagedPath(value: string | null | undefined, fallback = serverManagedDirectoryLabel) {
  return value?.trim() || fallback;
}
