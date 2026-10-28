import type { ApiInvoiceItemDto, ApiUnitDto } from "../../api/index.ts";
import { ApiInvoiceEditingLimitsContractDefaults } from "../../api/index.ts";
import { normalizeText, numberValue } from "../../ui/formUtils.ts";
import { documentSpareKeys, mapDocumentSpareFields } from "../../ui/documentSpareFields.ts";
import type { EditableInvoiceItemField, InvoiceItemColumnDefinition } from "./invoiceItemTableModel.ts";
import { invoiceItemEditableColumns } from "./invoiceItemTableModel.ts";
import { parseTabularClipboard } from "../../ui/tabularClipboard.ts";
import { invoiceItemPriceCalculationModes, invoiceItemUnitPriceMaximumScale, invoiceItemUnitPriceStandardScale, invoiceItemWeightScale, invoiceItemVolumeScale, roundTo, roundUnitPrice, roundWeight, roundVolume, normalizePriceCalculationMode, normalizeInvoiceItemPrices, copyInvoiceItemWithTotals } from "./invoiceItemCalculation.ts";
export { invoiceItemPriceCalculationModes, invoiceItemUnitPriceStandardScale, invoiceItemUnitPriceMaximumScale, invoiceItemWeightScale, invoiceItemVolumeScale, recalculateInvoiceItem, calculateInvoiceTotals, haveInvoiceTotalsInputsChanged, normalizePriceCalculationMode, hasChanged, roundMoney, roundTo, roundUnitPrice, roundWeight, roundVolume } from "./invoiceItemCalculation.ts";
export type { InvoiceItemPriceCalculationMode } from "./invoiceItemCalculation.ts";
export const maxInvoiceItemRows = ApiInvoiceEditingLimitsContractDefaults.maxItems;
export type InvoiceItemCellSelection = { rowIndex: number; field: EditableInvoiceItemField };
type UnitLookupSourceField = "unitEN" | "ctnUnitEN";
const invoiceItemHeaderHeightPx = 42;
const invoiceItemVirtualOverscanRows = 8;
const invoiceItemRowHeightPx = 42;
const blankWhenZeroInvoiceItemNumberFields = new Set<EditableInvoiceItemField>(["pcsPerCtn","cartons","length","width","height","volume","gwPerCtn","gwTotal","nwPerCtn","nwTotal","purchasePrice","purchaseTotal","taxRebateRate"]);
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
  const columnIndices = new Map(columns.map((column, index) => [column.field, index]));
  return Array.from(keys)
    .map((key) => parseCellKey(key, columns))
    .filter((cell): cell is InvoiceItemCellSelection => Boolean(cell && cell.rowIndex >= 0 && cell.rowIndex < itemCount))
    .sort((left, right) => left.rowIndex - right.rowIndex || columnIndices.get(left.field)! - columnIndices.get(right.field)!);
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

  const effectiveViewportHeight = Math.max(viewportHeight || 0, invoiceItemRowHeightPx * 8);
  const maximumScrollTop = Math.max(0, invoiceItemHeaderHeightPx + rowCount * invoiceItemRowHeightPx - effectiveViewportHeight);
  const boundedScrollTop = Math.min(Math.max(0, scrollTop), maximumScrollTop);
  const visibleBodyTop = Math.max(0, boundedScrollTop - invoiceItemHeaderHeightPx);
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

/** Linear bounds avoid spreading large selections into JavaScript call arguments. */
export function getInvoiceItemSelectionBounds(cells: readonly InvoiceItemCellSelection[], columns = invoiceItemEditableColumns) {
  const columnIndices = new Map(columns.map((column, index) => [column.field, index]));
  let rowStart = Infinity, rowEnd = -1, columnStart = Infinity, columnEnd = -1;
  for (const cell of cells) {
    const column = columnIndices.get(cell.field);
    if (column === undefined || cell.rowIndex < 0) continue;
    rowStart = Math.min(rowStart, cell.rowIndex);
    rowEnd = Math.max(rowEnd, cell.rowIndex);
    columnStart = Math.min(columnStart, column);
    columnEnd = Math.max(columnEnd, column);
  }
  return rowEnd < 0 ? null : { rowStart, rowEnd, columnStart, columnEnd };
}

export function buildSelectedCellsClipboardText(
  cells: InvoiceItemCellSelection[],
  items: ApiInvoiceItemDto[],
  columns = invoiceItemEditableColumns,
) {
  const bounds = getInvoiceItemSelectionBounds(cells, columns);
  if (!bounds) {
    return "";
  }

  const selectedKeys = new Set(cells.map(createCellKey));
  const { rowStart, rowEnd, columnStart, columnEnd } = bounds;
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
  return parseTabularClipboard(text.replace(/(?:\r?\n)+$/, ""), {
    characters: 4 * 1024 * 1024, rows: maxInvoiceItemRows, columns: invoiceItemEditableColumns.length,
    cells: maxInvoiceItemRows * invoiceItemEditableColumns.length,
  }).map(row => row.map(cell => cell.trim()));
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
  return {
    id: 0,
    invoiceId,
    poNumber: "",
    styleNo: "",
    styleName: "",
    fabricComposition: "",
    styleNameCN: "",
    brand: "",
    hsCode: "",
    origin: "",
    quantity: 0,
    unitEN: "",
    unitCN: "",
    pcsPerCtn: 0,
    cartons: 0,
    ctnUnitCN: "",
    ctnUnitEN: "",
    length: 0,
    width: 0,
    height: 0,
    volume: 0,
    gwPerCtn: 0,
    nwPerCtn: 0,
    gwTotal: 0,
    nwTotal: 0,
    priceCalculationMode: invoiceItemPriceCalculationModes.unitPriceDriven,
    unitPrice: 0,
    totalPrice: 0,
    purchasePrice: 0,
    purchaseTotal: 0,
    taxRebateRate: 0,
    taxRefundAmount: 0,
    ...mapDocumentSpareFields({}),
    customFieldsJson: "",
  };
}


const normalizedRows = new WeakMap<ApiInvoiceItemDto, { signature: string; value: ApiInvoiceItemDto }>();
export function normalizeInvoiceItemForSave(item: ApiInvoiceItemDto): ApiInvoiceItemDto {
  const signature = JSON.stringify(item);
  const cached = normalizedRows.get(item);
  if (cached?.signature === signature) return copyInvoiceItemWithTotals(cached.value);
  const normalized = {
    ...createEmptyInvoiceItem(numberValue(item.invoiceId)),
    ...item,
    brand: normalizeText(item.brand),
    cartons: numberValue(item.cartons),
    ctnUnitCN: normalizeText(item.ctnUnitCN),
    ctnUnitEN: normalizeText(item.ctnUnitEN),
    customFieldsJson: normalizeText(item.customFieldsJson),
    fabricComposition: normalizeText(item.fabricComposition),
    gwPerCtn: roundWeight(numberValue(item.gwPerCtn)),
    gwTotal: roundWeight(numberValue(item.gwTotal)),
    height: numberValue(item.height),
    hsCode: normalizeText(item.hsCode),
    id: numberValue(item.id),
    invoiceId: numberValue(item.invoiceId),
    length: numberValue(item.length),
    nwPerCtn: roundWeight(numberValue(item.nwPerCtn)),
    nwTotal: roundWeight(numberValue(item.nwTotal)),
    origin: normalizeText(item.origin),
    pcsPerCtn: numberValue(item.pcsPerCtn),
    poNumber: normalizeText(item.poNumber),
    purchasePrice: numberValue(item.purchasePrice),
    purchaseTotal: numberValue(item.purchaseTotal),
    quantity: numberValue(item.quantity),
    ...mapDocumentSpareFields(item, normalizeText),
    styleName: normalizeText(item.styleName),
    styleNameCN: normalizeText(item.styleNameCN),
    styleNo: normalizeText(item.styleNo),
    taxRebateRate: numberValue(item.taxRebateRate),
    totalPrice: numberValue(item.totalPrice),
    unitCN: normalizeText(item.unitCN),
    unitEN: normalizeText(item.unitEN),
    unitPrice: numberValue(item.unitPrice),
    priceCalculationMode: normalizePriceCalculationMode(item.priceCalculationMode),
    volume: roundVolume(numberValue(item.volume)),
    width: numberValue(item.width),
  };

  normalizeInvoiceItemPrices(normalized);
  normalizedRows.set(item, { signature, value: normalized });
  return copyInvoiceItemWithTotals(normalized);
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
    ...documentSpareKeys.map((key) => item[key]),
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
