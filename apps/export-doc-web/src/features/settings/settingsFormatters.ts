export function formatRuntimeDate(value?: string, timeZone?: string) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  try { return date.toLocaleString(undefined, timeZone ? { timeZone } : undefined); }
  catch { return value; }
}

export function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return "0 B";
  const units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  return `${index === 0 ? value : (value / 1024 ** index).toFixed(1)} ${units[index]}`;
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
