import assert from "node:assert/strict";
import { businessDateTimeLocalInputToIso as resolve, toBusinessDateTimeLocalInput as display } from "../apps/export-doc-web/src/ui/businessTime.ts";

for (const [zone, local] of [
  ["America/New_York", "2026-11-01T01:30"],
  ["Europe/Berlin", "2026-10-25T02:30"],
  ["Australia/Lord_Howe", "2026-04-05T01:45"],
]) assert.throws(() => resolve(local, zone), /重复时段/u);
for (const [zone, local] of [
  ["America/New_York", "2026-03-08T02:30"],
  ["America/Santiago", "2026-09-06T00:00"],
  ["Pacific/Apia", "2011-12-30T12:00"],
  ["UTC", "2026-02-30T12:00"],
]) assert.throws(() => resolve(local, zone), /无效|跳时/u);
for (const [zone, original] of [
  ["America/New_York", "2026-11-01T05:30:45.123Z"],
  ["America/New_York", "2026-11-01T06:30:00Z"],
  ["Europe/Berlin", "2026-10-25T00:30:00Z"],
  ["Europe/Berlin", "2026-10-25T01:30:00Z"],
  ["Asia/Shanghai", "2026-11-01T06:30:45.123456Z"],
]) assert.equal(resolve(display(original, zone), zone, original), original);
assert.equal(resolve("0099-06-01T12:00", "UTC"), "0099-06-01T12:00:00.000Z");
assert.equal(resolve("2026-11-01T14:30", "Asia/Shanghai"), "2026-11-01T06:30:00.000Z");
assert.equal(resolve("2026-11-01T03:30", "America/New_York", "2026-11-01T06:30:00Z"), "2026-11-01T08:30:00.000Z");
assert.equal(resolve("", "Asia/Shanghai", "2026-11-01T06:30:00Z"), undefined);
assert.throws(() => resolve("2026-11-01T03:30", "invalid/timezone"), /业务时区无效/u);
console.log("business time: gaps, ambiguity, precision, edits and early years passed");
