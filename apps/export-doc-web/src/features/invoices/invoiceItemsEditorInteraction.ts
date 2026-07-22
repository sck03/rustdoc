import type { KeyboardEvent } from "react";

export const invoiceItemHeaderHeightPx = 42;
export const invoiceItemRowHeightPx = 42;
export const invoiceItemVirtualizationThreshold = 90;

export type UnitLookupSourceField = "unitEN" | "ctnUnitEN";
type UnitLookupTargetField = "unitCN" | "ctnUnitCN";

type UnitLookupTarget = {
  sourceField: UnitLookupSourceField;
  targetField: UnitLookupTargetField;
  targetLabel: string;
};

export const invoiceItemUnitLookupTargets: Record<UnitLookupSourceField, UnitLookupTarget> = {
  unitEN: { sourceField: "unitEN", targetField: "unitCN", targetLabel: "中文单位" },
  ctnUnitEN: { sourceField: "ctnUnitEN", targetField: "ctnUnitCN", targetLabel: "包装中文单位" },
};

const arrowNavigationKeys = new Set(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"]);

export function isInvoiceItemVerticalNavigationKey(event: KeyboardEvent<HTMLElement>) {
  const nativeEvent = event.nativeEvent as globalThis.KeyboardEvent;
  if (nativeEvent.isComposing || event.ctrlKey || event.altKey || event.metaKey) return false;
  return event.key === "Enter" || event.key === "Tab";
}

export function isInvoiceItemCellInputTarget(target: EventTarget | null): target is HTMLInputElement {
  return target instanceof HTMLInputElement && target.classList.contains("item-cell-input");
}

export function isInvoiceItemArrowNavigationKey(event: KeyboardEvent<HTMLElement>) {
  const nativeEvent = event.nativeEvent as globalThis.KeyboardEvent;
  if (nativeEvent.isComposing || event.ctrlKey || event.altKey || event.metaKey) return false;
  return arrowNavigationKeys.has(event.key);
}

export function shouldMoveInvoiceItemCellByArrow(input: HTMLInputElement, key: string, extendSelection: boolean) {
  if (extendSelection || key === "ArrowUp" || key === "ArrowDown") return true;
  if (key !== "ArrowLeft" && key !== "ArrowRight") return false;
  if (input.type === "number") return true;

  const selectionStart = input.selectionStart ?? 0;
  const selectionEnd = input.selectionEnd ?? selectionStart;
  if (selectionStart !== selectionEnd) return false;
  return key === "ArrowLeft" ? selectionStart <= 0 : selectionEnd >= input.value.length;
}
