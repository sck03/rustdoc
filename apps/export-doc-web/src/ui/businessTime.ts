export const DEFAULT_BUSINESS_TIME_ZONE = "Asia/Shanghai";

type BusinessDateTimeParts = {
  year: number;
  month: number;
  day: number;
  hour: number;
  minute: number;
  second: number;
};

export function formatBusinessDateTime(
  value?: string | null,
  timeZone = DEFAULT_BUSINESS_TIME_ZONE,
  emptyValue = "-",
) {
  const instant = parseInstant(value);
  if (!instant) return emptyValue;
  try {
    return new Intl.DateTimeFormat("zh-CN", {
      timeZone,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hour12: false,
    }).format(instant);
  } catch {
    return emptyValue;
  }
}

export function toBusinessDateTimeLocalInput(
  value?: string | null,
  timeZone = DEFAULT_BUSINESS_TIME_ZONE,
) {
  const instant = parseInstant(value);
  if (!instant) return "";
  try {
    const parts = readBusinessParts(instant, timeZone);
    return `${pad(parts.year, 4)}-${pad(parts.month)}-${pad(parts.day)}T${pad(parts.hour)}:${pad(parts.minute)}`;
  } catch {
    return "";
  }
}

export function businessDateTimeLocalInputToIso(
  value: FormDataEntryValue | string | null,
  timeZone = DEFAULT_BUSINESS_TIME_ZONE,
) {
  const text = String(value ?? "").trim();
  if (!text) return undefined;
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(text);
  if (!match) throw new Error("业务日期时间格式无效，请重新选择。");

  const target: BusinessDateTimeParts = {
    year: Number(match[1]),
    month: Number(match[2]),
    day: Number(match[3]),
    hour: Number(match[4]),
    minute: Number(match[5]),
    second: Number(match[6] ?? 0),
  };
  const wallClockUtc = Date.UTC(
    target.year,
    target.month - 1,
    target.day,
    target.hour,
    target.minute,
    target.second,
  );
  if (!Number.isFinite(wallClockUtc)) throw new Error("业务日期时间无效，请重新选择。");

  let candidate = wallClockUtc;
  try {
    for (let attempt = 0; attempt < 3; attempt += 1) {
      candidate = wallClockUtc - readTimeZoneOffsetMilliseconds(new Date(candidate), timeZone);
    }
    const resolved = readBusinessParts(new Date(candidate), timeZone);
    if (!sameWallClock(resolved, target)) {
      throw new Error("所选业务日期时间位于时区跳时空档，请重新选择。");
    }
    return new Date(candidate).toISOString();
  } catch (error) {
    if (error instanceof Error && /业务日期时间|跳时空档/.test(error.message)) throw error;
    void error;
    throw new Error("业务时区无效，无法保存日期时间。");
  }
}

export function isPastInstant(value?: string | null, nowMilliseconds = Date.now()) {
  const instant = parseInstant(value);
  return instant ? instant.getTime() < nowMilliseconds : false;
}

function parseInstant(value?: string | null) {
  if (!value) return null;
  const instant = new Date(value);
  return Number.isNaN(instant.getTime()) ? null : instant;
}

function readTimeZoneOffsetMilliseconds(instant: Date, timeZone: string) {
  const parts = readBusinessParts(instant, timeZone);
  return Date.UTC(
    parts.year,
    parts.month - 1,
    parts.day,
    parts.hour,
    parts.minute,
    parts.second,
  ) - instant.getTime();
}

function readBusinessParts(instant: Date, timeZone: string): BusinessDateTimeParts {
  const values = new Map(
    new Intl.DateTimeFormat("en-CA", {
      timeZone,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hourCycle: "h23",
    }).formatToParts(instant).map((part) => [part.type, part.value]),
  );
  return {
    year: Number(values.get("year")),
    month: Number(values.get("month")),
    day: Number(values.get("day")),
    hour: Number(values.get("hour")),
    minute: Number(values.get("minute")),
    second: Number(values.get("second")),
  };
}

function sameWallClock(left: BusinessDateTimeParts, right: BusinessDateTimeParts) {
  return left.year === right.year && left.month === right.month && left.day === right.day &&
    left.hour === right.hour && left.minute === right.minute && left.second === right.second;
}

function pad(value: number, length = 2) {
  return String(value).padStart(length, "0");
}
