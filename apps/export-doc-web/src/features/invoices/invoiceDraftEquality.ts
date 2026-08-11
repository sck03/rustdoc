import type { ApiInvoiceDetailDto } from "../../api/index.ts";

export function areInvoiceDraftsEqual(left: ApiInvoiceDetailDto, right: ApiInvoiceDetailDto) {
  return areJsonValuesEqual(left, right);
}

function areJsonValuesEqual(left: unknown, right: unknown): boolean {
  if (Object.is(left, right)) return true;
  if (left === null || right === null || typeof left !== "object" || typeof right !== "object") return false;

  if (Array.isArray(left) || Array.isArray(right)) {
    if (!Array.isArray(left) || !Array.isArray(right) || left.length !== right.length) return false;
    return left.every((value, index) => areJsonValuesEqual(value, right[index]));
  }

  const leftRecord = left as Record<string, unknown>;
  const rightRecord = right as Record<string, unknown>;
  const leftKeys = Object.keys(leftRecord);
  const rightKeys = Object.keys(rightRecord);
  if (leftKeys.length !== rightKeys.length) return false;

  return leftKeys.every((key) =>
    Object.prototype.hasOwnProperty.call(rightRecord, key)
    && areJsonValuesEqual(leftRecord[key], rightRecord[key]));
}
