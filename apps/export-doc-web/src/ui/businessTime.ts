const DEFAULT_BUSINESS_TIME_ZONE = "Asia/Shanghai";

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
  original?: string | null,
) {
  const text = String(value ?? "").trim();
  if (!text) return undefined;
  // Editing another field must preserve an already resolved instant, including
  // its offset and sub-minute precision during a repeated local hour.
  if (original && parseInstant(original) && toBusinessDateTimeLocalInput(original, timeZone) === text) return original;
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
  const wallClockUtc = wallClockMilliseconds(target);
  if (!Number.isFinite(wallClockUtc)) throw new Error("业务日期时间无效，请重新选择。");

  let candidates: number[];
  try {
    const local = localClock(timeZone);
    candidates = offsetCandidates(wallClockUtc, local).filter(candidate => local(candidate) === wallClockUtc);
    if (!sameWallClock(readBusinessParts(new Date(wallClockUtc), "UTC"), target)) candidates = [];
  } catch {
    throw new Error("业务时区无效，无法保存日期时间。");
  }
  if (!candidates.length) throw new Error("所选业务日期时间无效或位于时区跳时空档，请重新选择。");
  if (candidates.length > 1) throw new Error("此时间处于夏令时回拨的重复时段，请选择无歧义的时间。");
  return new Date(candidates[0]).toISOString();
}

export function isPastInstant(value?: string | null, nowMilliseconds = Date.now()) {
  const instant = parseInstant(value);
  return instant ? instant.getTime() < nowMilliseconds : false;
}

// A calendar boundary chooses the earliest real instant of the date. Unlike
// an appointment it can advance across a missing midnight or a skipped date.
export function businessDayStart(day: string, timeZone: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) throw new Error("业务日期格式无效。");
  const midnight = Date.parse(`${day}T00:00:00Z`);
  if (!Number.isFinite(midnight) || new Date(midnight).toISOString().slice(0, 10) !== day) throw new Error("业务日期无效。");
  const local = localClock(timeZone);
  const candidates = offsetCandidates(midnight, local);
  const exact = candidates.filter(candidate => local(candidate) === midnight);
  if (exact.length) return new Date(Math.min(...exact)).toISOString();
  // Missing wall-clock values straddle a forward offset transition. Locate
  // that transition at second precision instead of assuming a one-hour gap.
  let low = Math.min(...candidates);
  let high = Math.max(...candidates);
  if (local(low) >= midnight || local(high) < midnight) throw new Error("业务日期超出时区支持范围。");
  while (high - low > 1000) {
    const middle = Math.floor((low + high) / 2000) * 1000;
    if (local(middle) >= midnight) high = middle;
    else low = middle;
  }
  return new Date(high).toISOString();
}

function localClock(timeZone: string) {
  const formatter = businessPartsFormatter(timeZone);
  return (instant: number) => wallClockMilliseconds(readBusinessParts(new Date(instant), timeZone, formatter));
}

function offsetCandidates(wallClock: number, local: (instant: number) => number) {
  const candidates = new Set<number>();
  // Both sides of half-hour, DST and date-line changes are inspected.
  for (let hours = -48; hours <= 48; hours += 12) {
    const sample = wallClock + hours * 3600000;
    candidates.add(wallClock - (local(sample) - sample));
  }
  return [...candidates];
}

function parseInstant(value?: string | null) {
  if (!value) return null;
  const instant = new Date(value);
  return Number.isNaN(instant.getTime()) ? null : instant;
}

function wallClockMilliseconds(parts: BusinessDateTimeParts) {
  const date = new Date(0);
  date.setUTCFullYear(parts.year, parts.month - 1, parts.day);
  date.setUTCHours(parts.hour, parts.minute, parts.second, 0);
  return date.getTime();
}

function businessPartsFormatter(timeZone: string) {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone, year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23",
  });
}

function readBusinessParts(instant: Date, timeZone: string, formatter = businessPartsFormatter(timeZone)): BusinessDateTimeParts {
  const values = new Map(formatter.formatToParts(instant).map((part) => [part.type, part.value]));
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
