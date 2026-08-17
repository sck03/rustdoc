export const defaultQueryPageSize = 50;
export const queryPageSizeOptions = [20, 50, 100, 200] as const;

export type QueryFilters = {
  startDate: string;
  endDate: string;
  customerId: string;
  exporterId: string;
  keyword: string;
  invoiceType: string;
  transportMode: string;
};

export function createDefaultQueryFilters(businessDate: string): QueryFilters {
  if (!isCalendarDate(businessDate)) {
    throw new Error("服务端业务日期无效。");
  }
  return {
    startDate: `${businessDate.slice(0, 7)}-01`,
    endDate: businessDate,
    customerId: "0",
    exporterId: "0",
    keyword: "",
    invoiceType: "",
    transportMode: "",
  };
}

export function readStoredQueryFilters(value: unknown, defaults: QueryFilters): QueryFilters {
  if (!value || typeof value !== "object") {
    return defaults;
  }

  const stored = value as Partial<Record<keyof QueryFilters, unknown>>;
  return normalizeQueryFilters({
    startDate: readStoredDate(stored.startDate, defaults.startDate),
    endDate: readStoredDate(stored.endDate, defaults.endDate),
    customerId: readString(stored.customerId, defaults.customerId),
    exporterId: readString(stored.exporterId, defaults.exporterId),
    keyword: readString(stored.keyword, defaults.keyword),
    invoiceType: readString(stored.invoiceType, defaults.invoiceType),
    transportMode: readString(stored.transportMode, defaults.transportMode),
  });
}

export function normalizeQueryPageSize(value: unknown) {
  const numericValue = typeof value === "number" ? value : Number(value);
  return queryPageSizeOptions.includes(numericValue as (typeof queryPageSizeOptions)[number])
    ? numericValue
    : defaultQueryPageSize;
}

export function normalizeQueryFilters(filters: QueryFilters): QueryFilters {
  return {
    startDate: filters.startDate,
    endDate: filters.endDate,
    customerId: filters.customerId,
    exporterId: filters.exporterId,
    keyword: filters.keyword.trim(),
    invoiceType: filters.invoiceType.trim(),
    transportMode: filters.transportMode.trim(),
  };
}

export function toApiQueryFilters(filters: QueryFilters) {
  const normalized = normalizeQueryFilters(filters);
  assertOptionalCalendarDate(normalized.startDate);
  assertOptionalCalendarDate(normalized.endDate);
  const customerId = Number(normalized.customerId);
  const exporterId = Number(normalized.exporterId);
  return {
    startDate: normalized.startDate || undefined,
    endDateExclusive: normalized.endDate ? nextCalendarDate(normalized.endDate) : undefined,
    customerId: Number.isFinite(customerId) && customerId > 0 ? customerId : undefined,
    exporterId: Number.isFinite(exporterId) && exporterId > 0 ? exporterId : undefined,
    keyword: normalized.keyword || undefined,
    invoiceType: normalized.invoiceType || undefined,
    transportMode: normalized.transportMode || undefined,
  };
}

export function nextCalendarDate(value: string) {
  if (!isCalendarDate(value)) {
    throw new Error("查询日期无效。");
  }
  if (value === "9999-12-31") {
    throw new Error("查询结束日期超出支持范围。");
  }

  const [year, month, day] = value.split("-").map(Number);
  const nextDate = new Date(0);
  nextDate.setUTCFullYear(year, month - 1, day + 1);
  return nextDate.toISOString().slice(0, 10);
}

function isCalendarDate(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) {
    return false;
  }

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12) {
    return false;
  }

  const daysInMonth = [31, isLeapYear(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return day >= 1 && day <= daysInMonth[month - 1];
}

function isLeapYear(year: number) {
  return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
}

function readString(value: unknown, fallback: string) {
  return typeof value === "string" ? value : fallback;
}

function readStoredDate(value: unknown, fallback: string) {
  return typeof value === "string" && (value === "" || isCalendarDate(value)) ? value : fallback;
}

function assertOptionalCalendarDate(value: string) {
  if (value && !isCalendarDate(value)) {
    throw new Error("查询日期无效。");
  }
}
