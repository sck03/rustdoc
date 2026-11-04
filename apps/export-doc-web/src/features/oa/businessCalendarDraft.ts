import type { OaBusinessCalendar } from "../../api/index.ts";

export const calendarDateKeys = ["holidays", "additionalWorkingDays"] as const;
export type CalendarDateKey = typeof calendarDateKeys[number];
export type CalendarDateInputs = Record<CalendarDateKey, string>;
export const calendarEditableFields = ["timeZone", "workingWeekdays", "intervals", ...calendarDateKeys] as const;

export function calendarDateInputs(value?: OaBusinessCalendar): CalendarDateInputs {
  return { holidays: value?.holidays.join("\n") ?? "", additionalWorkingDays: value?.additionalWorkingDays.join("\n") ?? "" };
}
