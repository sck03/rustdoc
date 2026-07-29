import type { ApiInvoiceDetailDto, ApiInvoiceItemDto, ApiProductDto, ApiUnitDto } from "../../api/index.ts";
import { normalizeText, numberValue } from "../../ui/formUtils.ts";
import type { EditableInvoiceItemField, InvoiceItemColumnDefinition } from "./invoiceItemTableModel.ts";
import { invoiceItemEditableColumns } from "./invoiceItemTableModel.ts";
export type InvoiceItemCellSelection = { rowIndex: number; field: EditableInvoiceItemField };
type UnitLookupSourceField = "unitEN" | "ctnUnitEN";
const invoiceItemHeaderHeightPx = 42;
const invoiceItemVirtualizationThreshold = 90;
const invoiceItemVirtualOverscanRows = 8;
const invoiceItemRowHeightPx = 42;
const blankWhenZeroInvoiceItemNumberFields = new Set<EditableInvoiceItemField>(["pcsPerCtn","cartons","length","width","height","volume","gwPerCtn","gwTotal","nwPerCtn","nwTotal","purchasePrice","purchaseTotal","taxRebateRate"]);
export const invoiceItemPriceCalculationModes = {
  unitPriceDriven: "UnitPriceDriven",
  lineAmountDriven: "LineAmountDriven",
} as const;
export type InvoiceItemPriceCalculationMode = typeof invoiceItemPriceCalculationModes[keyof typeof invoiceItemPriceCalculationModes];
export const invoiceItemUnitPriceStandardScale = 2;
export const invoiceItemUnitPriceMaximumScale = 5;
export const invoiceItemWeightScale = 2;
export const invoiceItemVolumeScale = 3;
const invoiceItemWeightFields = new Set<EditableInvoiceItemField>(["gwPerCtn", "gwTotal", "nwPerCtn", "nwTotal"]);

export function isUnitLookupSourceField(field: EditableInvoiceItemField): field is UnitLookupSourceField {
  return field === "unitEN" || field === "ctnUnitEN";
}

export function buildUnitCandidateLookup(units: ApiUnitDto[]) {
  const lookup = new Map<string, string[]>();
  for (const unit of units) {
    const unitEnKey = normalizeUnitEnglishKey(unit.nameEN);
    const nameCN = normalizeText(unit.nameCN);
    if (!unitEnKey || !nameCN) {
      continue;
    }

    const candidates = lookup.get(unitEnKey) ?? [];
    if (candidates.some((candidate) => candidate.toLowerCase() === nameCN.toLowerCase())) {
      continue;
    }

    candidates.push(nameCN);
    lookup.set(unitEnKey, candidates);
  }

  return lookup;
}

export function findChineseUnitCandidates(unitLookup: Map<string, string[]>, unitEn: string) {
  const unitEnKey = normalizeUnitEnglishKey(unitEn);
  return unitEnKey ? unitLookup.get(unitEnKey) ?? [] : [];
}

export function normalizeUnitEnglishKey(value?: string) {
  return normalizeText(value).toUpperCase();
}

export function createCellKey(cell: InvoiceItemCellSelection) {
  return `${cell.rowIndex}:${cell.field}`;
}

export function parseCellKey(key: string, columns = invoiceItemEditableColumns): InvoiceItemCellSelection | null {
  const [rowText, fieldText] = key.split(":");
  const rowIndex = Number(rowText);
  const field = fieldText as EditableInvoiceItemField;
  if (!Number.isInteger(rowIndex) || !columns.some((column) => column.field === field)) {
    return null;
  }

  return { rowIndex, field };
}

export function readSelectedCells(keys: Set<string>, itemCount: number, columns = invoiceItemEditableColumns) {
  return Array.from(keys)
    .map((key) => parseCellKey(key, columns))
    .filter((cell): cell is InvoiceItemCellSelection => Boolean(cell && cell.rowIndex >= 0 && cell.rowIndex < itemCount))
    .sort((left, right) => compareInvoiceItemCells(left, right, columns));
}

export function canFillDownSelectedCells(cells: InvoiceItemCellSelection[]) {
  if (cells.length < 2) {
    return false;
  }

  const rowsByField = new Map<EditableInvoiceItemField, Set<number>>();
  for (const cell of cells) {
    const rows = rowsByField.get(cell.field) ?? new Set<number>();
    rows.add(cell.rowIndex);
    rowsByField.set(cell.field, rows);
  }

  return Array.from(rowsByField.values()).some((rows) => rows.size >= 2);
}

export function compareInvoiceItemCells(
  left: InvoiceItemCellSelection,
  right: InvoiceItemCellSelection,
  columns = invoiceItemEditableColumns,
) {
  const rowDelta = left.rowIndex - right.rowIndex;
  return rowDelta !== 0 ? rowDelta : getInvoiceItemColumnIndex(left.field, columns) - getInvoiceItemColumnIndex(right.field, columns);
}

export function buildCellRangeKeys(anchor: InvoiceItemCellSelection, target: InvoiceItemCellSelection, columns = invoiceItemEditableColumns) {
  const anchorColumnIndex = getInvoiceItemColumnIndex(anchor.field, columns);
  const targetColumnIndex = getInvoiceItemColumnIndex(target.field, columns);
  const rowStart = Math.min(anchor.rowIndex, target.rowIndex);
  const rowEnd = Math.max(anchor.rowIndex, target.rowIndex);
  const columnStart = Math.min(anchorColumnIndex, targetColumnIndex);
  const columnEnd = Math.max(anchorColumnIndex, targetColumnIndex);
  const keys = new Set<string>();

  for (let rowIndex = rowStart; rowIndex <= rowEnd; rowIndex += 1) {
    for (let columnIndex = columnStart; columnIndex <= columnEnd; columnIndex += 1) {
      const field = columns[columnIndex]?.field;
      if (field) {
        keys.add(createCellKey({ rowIndex, field }));
      }
    }
  }

  return keys;
}

export function getInvoiceItemColumnIndex(field: EditableInvoiceItemField, columns = invoiceItemEditableColumns) {
  return Math.max(0, columns.findIndex((column) => column.field === field));
}

export function normalizeInvoiceItemBlankRowCount(value: number) {
  if (!Number.isFinite(value)) {
    return 0;
  }

  return Math.max(0, Math.min(500, Math.trunc(value)));
}

export function calculateInvoiceItemVirtualRange(
  rowCount: number,
  scrollTop: number,
  viewportHeight: number,
  shouldVirtualize: boolean,
) {
  if (!shouldVirtualize || rowCount <= 0) {
    return {
      startIndex: 0,
      endIndex: rowCount,
      topSpacerHeight: 0,
      bottomSpacerHeight: 0,
    };
  }

  const visibleBodyTop = Math.max(0, scrollTop - invoiceItemHeaderHeightPx);
  const effectiveViewportHeight = Math.max(viewportHeight || 0, invoiceItemRowHeightPx * 8);
  const startIndex = Math.max(0, Math.floor(visibleBodyTop / invoiceItemRowHeightPx) - invoiceItemVirtualOverscanRows);
  const endIndex = Math.min(
    rowCount,
    Math.ceil((visibleBodyTop + effectiveViewportHeight) / invoiceItemRowHeightPx) + invoiceItemVirtualOverscanRows,
  );

  return {
    startIndex,
    endIndex,
    topSpacerHeight: startIndex * invoiceItemRowHeightPx,
    bottomSpacerHeight: Math.max(0, rowCount - endIndex) * invoiceItemRowHeightPx,
  };
}

export function buildSelectedCellsClipboardText(
  cells: InvoiceItemCellSelection[],
  items: ApiInvoiceItemDto[],
  columns = invoiceItemEditableColumns,
) {
  if (cells.length === 0) {
    return "";
  }

  const selectedKeys = new Set(cells.map(createCellKey));
  const rowStart = Math.min(...cells.map((cell) => cell.rowIndex));
  const rowEnd = Math.max(...cells.map((cell) => cell.rowIndex));
  const columnStart = Math.min(...cells.map((cell) => getInvoiceItemColumnIndex(cell.field, columns)));
  const columnEnd = Math.max(...cells.map((cell) => getInvoiceItemColumnIndex(cell.field, columns)));
  const lines: string[] = [];

  for (let rowIndex = rowStart; rowIndex <= rowEnd; rowIndex += 1) {
    const rowValues: string[] = [];
    for (let columnIndex = columnStart; columnIndex <= columnEnd; columnIndex += 1) {
      const column = columns[columnIndex];
      const key = column ? createCellKey({ rowIndex, field: column.field }) : "";
      rowValues.push(column && selectedKeys.has(key) ? readItemClipboardValue(items[rowIndex], column) : "");
    }

    lines.push(rowValues.join("\t"));
  }

  return lines.join("\n");
}

export function calculateInvoiceItemTableMinWidth(columns: InvoiceItemColumnDefinition[]) {
  return Math.max(420, 148 + columns.reduce((total, column) => total + getInvoiceItemColumnWidth(column), 0));
}

export function getInvoiceItemColumnWidth(column: InvoiceItemColumnDefinition) {
  if (column.colClassName.includes("item-wide-col")) {
    return 170;
  }

  if (column.colClassName.includes("item-number-col")) {
    return 108;
  }

  return 112;
}

export function readItemClipboardValue(item: ApiInvoiceItemDto | undefined, column: InvoiceItemColumnDefinition) {
  if (!item) {
    return "";
  }

  const value = item[column.field];
  if (column.kind === "number") {
    return typeof value === "number" && Number.isFinite(value) ? String(value) : "";
  }

  return sanitizeClipboardCell(typeof value === "string" ? value : "");
}

export function sanitizeClipboardCell(value: string) {
  return value.replace(/\r?\n/g, " ").replace(/\t/g, " ");
}

export function parseInvoiceItemClipboardRows(text: string): string[][] {
  const normalized = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n").replace(/\n+$/, "");
  if (!normalized) {
    return [];
  }

  return normalized.split("\n").map((line) => line.split("\t").map((cell) => cell.trim()));
}

export function readItemTextValue(item: ApiInvoiceItemDto, field: EditableInvoiceItemField) {
  const value = item[field];
  return typeof value === "string" ? value : "";
}

export function readItemNumberValue(item: ApiInvoiceItemDto, field: EditableInvoiceItemField) {
  const value = item[field];
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return undefined;
  }

  if (value === 0 && (blankWhenZeroInvoiceItemNumberFields.has(field) || !isMeaningfulInvoiceItem(item))) {
    return undefined;
  }

  return value;
}

export function invoiceItemNumberInputValue(value?: number) {
  return Number.isFinite(value) ? String(value) : "";
}

export function invoiceItemUnitPriceDisplayValue(value?: number) {
  if (!Number.isFinite(value)) {
    return "";
  }

  const normalized = roundUnitPrice(Number(value));
  if (roundTo(normalized, invoiceItemUnitPriceStandardScale) === normalized) {
    return normalized.toFixed(invoiceItemUnitPriceStandardScale);
  }

  return normalized.toFixed(invoiceItemUnitPriceMaximumScale).replace(/0+$/, "");
}

export function invoiceItemNumberDisplayValue(field: EditableInvoiceItemField, value?: number) {
  if (!Number.isFinite(value)) {
    return "";
  }

  if (field === "unitPrice") {
    return invoiceItemUnitPriceDisplayValue(value);
  }

  if (invoiceItemWeightFields.has(field)) {
    return invoiceItemWeightDisplayValue(value);
  }

  if (field === "volume") {
    return invoiceItemVolumeDisplayValue(value);
  }

  return invoiceItemNumberInputValue(value);
}

export function invoiceItemWeightDisplayValue(value?: number) {
  return Number.isFinite(value) ? roundWeight(Number(value)).toFixed(invoiceItemWeightScale) : "";
}

export function invoiceItemVolumeDisplayValue(value?: number) {
  return Number.isFinite(value) ? roundVolume(Number(value)).toFixed(invoiceItemVolumeScale) : "";
}

export function readInvoiceItemNumberInput(value: string) {
  if (!value.trim()) {
    return undefined;
  }

  const next = Number(value);
  return Number.isFinite(next) ? next : undefined;
}

export function createEmptyInvoiceItem(invoiceId = 0): ApiInvoiceItemDto {
  const item: Partial<ApiInvoiceItemDto> = {
    id: 0,
    invoiceId,
    ctnUnitCN: "",
    ctnUnitEN: "",
    styleNo: "",
    styleName: "",
    unitEN: "",
    unitCN: "",
    priceCalculationMode: invoiceItemPriceCalculationModes.unitPriceDriven,
  };

  return item as ApiInvoiceItemDto;
}

export function recalculateInvoiceItem(item: ApiInvoiceItemDto, changedFields: string[]): ApiInvoiceItemDto {
  const next = { ...item };
  const quantity = numberValue(next.quantity);
  const cartons = numberValue(next.cartons);
  const totalPriceChanged = hasChanged(changedFields, ["totalPrice"]);
  const unitPriceChanged = hasChanged(changedFields, ["unitPrice"]);
  const quantityChanged = hasChanged(changedFields, ["quantity"]);
  const volumeChanged = hasChanged(changedFields, ["volume"]);
  const grossWeightChanged = hasChanged(changedFields, ["gwPerCtn", "gwTotal"]);
  const netWeightChanged = hasChanged(changedFields, ["nwPerCtn", "nwTotal"]);

  if (hasChanged(changedFields, ["gwPerCtn"])) {
    next.gwPerCtn = roundWeight(numberValue(next.gwPerCtn));
  }
  if (hasChanged(changedFields, ["nwPerCtn"])) {
    next.nwPerCtn = roundWeight(numberValue(next.nwPerCtn));
  }
  if (hasChanged(changedFields, ["gwTotal"])) {
    next.gwTotal = roundWeight(numberValue(next.gwTotal));
  }
  if (hasChanged(changedFields, ["nwTotal"])) {
    next.nwTotal = roundWeight(numberValue(next.nwTotal));
  }
  if (volumeChanged) {
    next.volume = roundVolume(numberValue(next.volume));
  }

  if (totalPriceChanged) {
    next.priceCalculationMode = invoiceItemPriceCalculationModes.lineAmountDriven;
  } else if (unitPriceChanged) {
    next.priceCalculationMode = invoiceItemPriceCalculationModes.unitPriceDriven;
  } else {
    next.priceCalculationMode = normalizePriceCalculationMode(next.priceCalculationMode);
  }

  if (hasChanged(changedFields, ["quantity", "pcsPerCtn"])) {
    const pcsPerCtn = numberValue(next.pcsPerCtn);
    if (pcsPerCtn > 0) {
      next.cartons = Math.ceil(quantity / pcsPerCtn);
    }
  }

  const effectiveCartons = numberValue(next.cartons ?? cartons);
  if (quantityChanged || totalPriceChanged || unitPriceChanged) {
    if (next.priceCalculationMode === invoiceItemPriceCalculationModes.lineAmountDriven) {
      next.totalPrice = roundMoney(numberValue(next.totalPrice));
      next.unitPrice = quantity > 0 ? roundUnitPrice(numberValue(next.totalPrice) / quantity) : 0;
    } else {
      next.unitPrice = roundUnitPrice(numberValue(next.unitPrice));
      next.totalPrice = roundMoney(quantity * numberValue(next.unitPrice));
    }
  }

  if (hasChanged(changedFields, ["quantity", "purchasePrice"])) {
    next.purchaseTotal = roundMoney(quantity * numberValue(next.purchasePrice));
  }

  if (hasChanged(changedFields, ["quantity", "pcsPerCtn", "cartons", "length", "width", "height"])) {
    next.volume =
      numberValue(next.length) > 0 && numberValue(next.width) > 0 && numberValue(next.height) > 0 && effectiveCartons > 0
        ? roundVolume((numberValue(next.length) * numberValue(next.width) * numberValue(next.height) * effectiveCartons) / 1000000)
        : 0;
  }

  if (hasChanged(changedFields, ["quantity", "pcsPerCtn", "cartons", "gwPerCtn"])) {
    next.gwTotal =
      numberValue(next.gwPerCtn) > 0 && effectiveCartons > 0
        ? roundWeight(numberValue(next.gwPerCtn) * effectiveCartons)
        : 0;
  }

  if (hasChanged(changedFields, ["quantity", "pcsPerCtn", "cartons", "nwPerCtn"])) {
    next.nwTotal =
      numberValue(next.nwPerCtn) > 0 && effectiveCartons > 0
        ? roundWeight(numberValue(next.nwPerCtn) * effectiveCartons)
        : 0;
  }

  if (!grossWeightChanged && hasChanged(changedFields, ["quantity", "pcsPerCtn", "cartons"])) {
    next.gwTotal = roundWeight(numberValue(next.gwTotal));
  }
  if (!netWeightChanged && hasChanged(changedFields, ["quantity", "pcsPerCtn", "cartons"])) {
    next.nwTotal = roundWeight(numberValue(next.nwTotal));
  }

  return next;
}

export function calculateInvoiceTotals(
  items: ApiInvoiceItemDto[],
  exchangeRate?: number,
  currency?: string,
): Partial<ApiInvoiceDetailDto> {
  const totals = items.reduce(
    (current, item) => ({
      amount: current.amount + numberValue(item.totalPrice),
      cartons: current.cartons + numberValue(item.cartons),
      grossWeight: current.grossWeight + numberValue(item.gwTotal),
      netWeight: current.netWeight + numberValue(item.nwTotal),
      purchaseAmount: current.purchaseAmount + numberValue(item.purchaseTotal),
      quantity: current.quantity + numberValue(item.quantity),
      taxRefundAmount:
        current.taxRefundAmount + (numberValue(item.purchaseTotal) / 1.13) * (numberValue(item.taxRebateRate) / 100),
      volume: current.volume + numberValue(item.volume),
    }),
    {
      amount: 0,
      cartons: 0,
      grossWeight: 0,
      netWeight: 0,
      purchaseAmount: 0,
      quantity: 0,
      taxRefundAmount: 0,
      volume: 0,
    },
  );

  const configuredRate = numberValue(exchangeRate);
  const finalAmount = roundMoney(totals.amount);
  const effectiveRate = configuredRate > 0
    ? configuredRate
    : normalizeText(currency).toUpperCase() === "CNY"
      ? 1
      : 0;

  return {
    totalAmount: finalAmount,
    totalCartons: roundMoney(totals.cartons),
    totalGrossWeight: roundWeight(totals.grossWeight),
    totalNetWeight: roundWeight(totals.netWeight),
    totalProfit: effectiveRate > 0
      ? roundMoney(finalAmount * effectiveRate - totals.purchaseAmount + totals.taxRefundAmount)
      : 0,
    totalPurchaseAmount: roundMoney(totals.purchaseAmount),
    totalQuantity: roundMoney(totals.quantity),
    totalTaxRefundAmount: roundMoney(totals.taxRefundAmount),
    totalVolume: roundVolume(totals.volume),
  };
}

export function normalizeInvoiceItemForSave(item: ApiInvoiceItemDto): ApiInvoiceItemDto {
  const normalized = {
    ...createEmptyInvoiceItem(numberValue(item.invoiceId)),
    ...item,
    brand: normalizeText(item.brand),
    cartons: normalizeOptionalInvoiceItemNumber(item.cartons),
    ctnUnitCN: normalizeText(item.ctnUnitCN),
    ctnUnitEN: normalizeText(item.ctnUnitEN),
    customFieldsJson: normalizeText(item.customFieldsJson),
    fabricComposition: normalizeText(item.fabricComposition),
    gwPerCtn: roundOptionalWeight(item.gwPerCtn),
    gwTotal: roundOptionalWeight(item.gwTotal),
    height: normalizeOptionalInvoiceItemNumber(item.height),
    hsCode: normalizeText(item.hsCode),
    id: numberValue(item.id),
    invoiceId: numberValue(item.invoiceId),
    length: normalizeOptionalInvoiceItemNumber(item.length),
    nwPerCtn: roundOptionalWeight(item.nwPerCtn),
    nwTotal: roundOptionalWeight(item.nwTotal),
    origin: normalizeText(item.origin),
    pcsPerCtn: normalizeOptionalInvoiceItemNumber(item.pcsPerCtn),
    poNumber: normalizeText(item.poNumber),
    purchasePrice: normalizeOptionalInvoiceItemNumber(item.purchasePrice),
    purchaseTotal: normalizeOptionalInvoiceItemNumber(item.purchaseTotal),
    quantity: numberValue(item.quantity),
    spare1: normalizeText(item.spare1),
    spare2: normalizeText(item.spare2),
    spare3: normalizeText(item.spare3),
    styleName: normalizeText(item.styleName),
    styleNameCN: normalizeText(item.styleNameCN),
    styleNo: normalizeText(item.styleNo),
    taxRebateRate: normalizeOptionalInvoiceItemNumber(item.taxRebateRate),
    totalPrice: numberValue(item.totalPrice),
    unitCN: normalizeText(item.unitCN),
    unitEN: normalizeText(item.unitEN),
    unitPrice: numberValue(item.unitPrice),
    priceCalculationMode: normalizePriceCalculationMode(item.priceCalculationMode),
    volume: roundOptionalVolume(item.volume),
    width: normalizeOptionalInvoiceItemNumber(item.width),
  };

  return recalculateInvoiceItem(normalized, ["quantity"]);
}

export function normalizePriceCalculationMode(value?: string): InvoiceItemPriceCalculationMode {
  return value?.trim().toLowerCase() === invoiceItemPriceCalculationModes.lineAmountDriven.toLowerCase()
    ? invoiceItemPriceCalculationModes.lineAmountDriven
    : invoiceItemPriceCalculationModes.unitPriceDriven;
}

export function normalizeOptionalInvoiceItemNumber(value?: number) {
  return Number.isFinite(value) && Number(value) !== 0 ? Number(value) : undefined;
}

export function isMeaningfulInvoiceItem(item: ApiInvoiceItemDto) {
  const textValues = [
    item.brand,
    item.ctnUnitCN,
    item.ctnUnitEN,
    item.fabricComposition,
    item.hsCode,
    item.origin,
    item.poNumber,
    item.spare1,
    item.spare2,
    item.spare3,
    item.styleName,
    item.styleNameCN,
    item.styleNo,
    item.unitCN,
    item.unitEN,
  ];

  const numberValues = [
    item.cartons,
    item.gwPerCtn,
    item.gwTotal,
    item.height,
    item.length,
    item.nwPerCtn,
    item.nwTotal,
    item.pcsPerCtn,
    item.purchasePrice,
    item.purchaseTotal,
    item.quantity,
    item.taxRebateRate,
    item.totalPrice,
    item.unitPrice,
    item.volume,
    item.width,
  ];

  return item.id > 0 || textValues.some((value) => Boolean(value?.trim())) || numberValues.some((value) => numberValue(value) !== 0);
}

export function hasChanged(changedFields: string[], fields: string[]) {
  return changedFields.some((field) => fields.includes(field));
}

export function roundMoney(value: number) {
  return roundTo(value, 2);
}

export function roundUnitPrice(value: number) {
  return roundTo(value, invoiceItemUnitPriceMaximumScale);
}

export function roundWeight(value: number) {
  return roundTo(value, invoiceItemWeightScale);
}

export function roundVolume(value: number) {
  return roundTo(value, invoiceItemVolumeScale);
}

export function roundMeasure(value: number) {
  return roundVolume(value);
}

function roundOptionalWeight(value?: number) {
  const normalized = normalizeOptionalInvoiceItemNumber(value);
  return normalized === undefined ? undefined : roundWeight(normalized);
}

function roundOptionalVolume(value?: number) {
  const normalized = normalizeOptionalInvoiceItemNumber(value);
  return normalized === undefined ? undefined : roundVolume(normalized);
}

export function roundTo(value: number, digits: number) {
  return Number.isFinite(value) ? Number(value.toFixed(digits)) : 0;
}
