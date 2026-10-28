import type { ApiInvoiceDetailDto, ApiInvoiceItemDto } from "../../api/index.ts";
import { ExactDecimal } from "../../ui/decimalSum.ts";
import { normalizeText, numberValue } from "../../ui/formUtils.ts";

export const invoiceItemPriceCalculationModes = { unitPriceDriven: "UnitPriceDriven", lineAmountDriven: "LineAmountDriven" } as const;
export type InvoiceItemPriceCalculationMode = typeof invoiceItemPriceCalculationModes[keyof typeof invoiceItemPriceCalculationModes];
export const invoiceItemUnitPriceStandardScale = 2;
export const invoiceItemUnitPriceMaximumScale = 5;
export const invoiceItemWeightScale = 2;
export const invoiceItemVolumeScale = 3;
const decimal = (value?: number | null) => ExactDecimal.from(numberValue(value));

export function roundTo(value: number, digits: number) { return Number.isFinite(value) ? ExactDecimal.from(value).round(digits).toNumber() : 0; }
export const roundMoney = (value: number) => roundTo(value, 2);
export const roundUnitPrice = (value: number) => roundTo(value, invoiceItemUnitPriceMaximumScale);
export const roundWeight = (value: number) => roundTo(value, invoiceItemWeightScale);
export const roundVolume = (value: number) => roundTo(value, invoiceItemVolumeScale);
export function hasChanged(changed: string[], fields: string[]) { return changed.some(field => fields.includes(field)); }
export function normalizePriceCalculationMode(value?: string): InvoiceItemPriceCalculationMode {
  return value?.trim().toLowerCase() === "lineamountdriven" ? "LineAmountDriven" : "UnitPriceDriven";
}
export function normalizeInvoiceItemPrices(item: ApiInvoiceItemDto) {
  const quantity = numberValue(item.quantity);
  if (item.priceCalculationMode === "LineAmountDriven") {
    item.totalPrice = roundMoney(numberValue(item.totalPrice));
    item.unitPrice = quantity > 0 ? decimal(item.totalPrice).divide(quantity, 5).toNumber() : 0;
  } else {
    item.unitPrice = roundUnitPrice(numberValue(item.unitPrice));
    item.totalPrice = decimal(quantity).multiply(item.unitPrice).round(2).toNumber();
  }
}

export function recalculateInvoiceItem(item: ApiInvoiceItemDto, changedFields: string[]): ApiInvoiceItemDto {
  const next = { ...item };
  const changed = (...fields: string[]) => hasChanged(changedFields, fields);
  const quantity = numberValue(next.quantity);
  for (const field of ["gwPerCtn", "nwPerCtn", "gwTotal", "nwTotal"] as const) {
    if (changed(field)) next[field] = roundWeight(numberValue(next[field]));
  }
  if (changed("volume")) next.volume = roundVolume(numberValue(next.volume));
  next.priceCalculationMode = changed("totalPrice") ? "LineAmountDriven" : changed("unitPrice") ? "UnitPriceDriven" : normalizePriceCalculationMode(next.priceCalculationMode);
  if (changed("quantity", "pcsPerCtn") && numberValue(next.pcsPerCtn) > 0) next.cartons = decimal(quantity).divide(numberValue(next.pcsPerCtn)).ceil();
  const cartons = numberValue(next.cartons);
  if (changed("quantity", "totalPrice", "unitPrice")) normalizeInvoiceItemPrices(next);
  if (changed("quantity", "purchasePrice")) next.purchaseTotal = decimal(quantity).multiply(numberValue(next.purchasePrice)).round(2).toNumber();
  if (!changed("volume") && changed("quantity", "pcsPerCtn", "cartons", "length", "width", "height")) {
    next.volume = [next.length, next.width, next.height, cartons].every(value => numberValue(value) > 0)
      ? decimal(next.length).multiply(numberValue(next.width)).multiply(numberValue(next.height)).multiply(cartons).divide(1_000_000, 3).toNumber() : 0;
  }
  for (const [perCarton, total] of [["gwPerCtn", "gwTotal"], ["nwPerCtn", "nwTotal"]] as const) {
    if (!changed(total) && changed("quantity", "pcsPerCtn", "cartons", perCarton)) {
      next[total] = numberValue(next[perCarton]) > 0 && cartons > 0 ? decimal(next[perCarton]).multiply(cartons).round(2).toNumber() : 0;
    }
  }
  return next;
}

type RowTotals = { amount: ExactDecimal; cartons: ExactDecimal; grossWeight: ExactDecimal; netWeight: ExactDecimal; purchase: ExactDecimal; quantity: ExactDecimal; refund: ExactDecimal; volume: ExactDecimal };
const totalInputFields = ["totalPrice", "cartons", "gwTotal", "nwTotal", "purchaseTotal", "quantity", "taxRebateRate", "volume"] as const;
export function haveInvoiceTotalsInputsChanged(previous: readonly ApiInvoiceItemDto[], next: readonly ApiInvoiceItemDto[]): boolean {
  return previous.length !== next.length || previous.some((item, index) => item !== next[index]
    && totalInputFields.some(field => numberValue(item[field]) !== numberValue(next[index][field])));
}
const rowTotals = new WeakMap<ApiInvoiceItemDto, { fields: number[]; totals: RowTotals }>();
function itemTotals(item: ApiInvoiceItemDto): RowTotals {
  const fields = totalInputFields.map(field => numberValue(item[field]));
  const cached = rowTotals.get(item);
  if (cached && fields.every((value, index) => value === cached.fields[index])) return cached.totals;
  const [amount, cartons, grossWeight, netWeight, purchase, quantity, rate, volume] = fields.map(decimal);
  const totals = { amount, cartons, grossWeight, netWeight, purchase, quantity, volume, refund: purchase.divide("1.13").multiply(rate.divide(100)) };
  rowTotals.set(item, { fields, totals });
  return totals;
}

/** Copies remain editable while unchanged row calculations share a weakly held cache. */
export function copyInvoiceItemWithTotals(item: ApiInvoiceItemDto): ApiInvoiceItemDto {
  itemTotals(item);
  const copy = { ...item };
  rowTotals.set(copy, rowTotals.get(item)!);
  return copy;
}

export function calculateInvoiceTotals(items: ApiInvoiceItemDto[], exchangeRate?: number | null, currency?: string): Partial<ApiInvoiceDetailDto> {
  const totals = { amount: decimal(0), cartons: decimal(0), grossWeight: decimal(0), netWeight: decimal(0), purchase: decimal(0), quantity: decimal(0), refund: decimal(0), volume: decimal(0) };
  const keys = Object.keys(totals) as Array<keyof RowTotals>;
  for (const item of items) {
    const row = itemTotals(item);
    for (const key of keys) totals[key] = totals[key].add(row[key]);
  }
  const amount = totals.amount.round(2);
  const rate = numberValue(exchangeRate) > 0 ? numberValue(exchangeRate) : normalizeText(currency).toUpperCase() === "CNY" ? 1 : 0;
  return { totalAmount: amount.toNumber(), totalCartons: totals.cartons.round(2).toNumber(), totalGrossWeight: totals.grossWeight.round(2).toNumber(),
    totalNetWeight: totals.netWeight.round(2).toNumber(), totalPurchaseAmount: totals.purchase.round(2).toNumber(), totalQuantity: totals.quantity.round(2).toNumber(),
    totalTaxRefundAmount: totals.refund.round(2).toNumber(), totalVolume: totals.volume.round(3).toNumber(),
    totalProfit: rate > 0 ? amount.multiply(rate).subtract(totals.purchase).add(totals.refund).round(2).toNumber() : 0 };
}
