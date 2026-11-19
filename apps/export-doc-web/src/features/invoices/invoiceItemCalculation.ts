import type { ApiInvoiceDetailDto, ApiInvoiceItemDto } from "../../api/index.ts";
import { ExactDecimal, decimalText } from "../../ui/decimalSum.ts";
import { normalizeText } from "../../ui/formUtils.ts";

export const invoiceItemPriceCalculationModes = { unitPriceDriven: "UnitPriceDriven", lineAmountDriven: "LineAmountDriven" } as const;
export type InvoiceItemPriceCalculationMode = typeof invoiceItemPriceCalculationModes[keyof typeof invoiceItemPriceCalculationModes];
export const invoiceItemUnitPriceStandardScale = 2;
export const invoiceItemUnitPriceMaximumScale = 5;
export const invoiceItemWeightScale = 2;
export const invoiceItemVolumeScale = 3;
const decimal = (value?: string | null) => ExactDecimal.from(decimalText(value));

export const roundTo = (value: string, digits: number) => decimal(value).round(digits).normalize().toString();
export const roundMoney = (value: string) => roundTo(value, 2);
export const roundUnitPrice = (value: string) => roundTo(value, invoiceItemUnitPriceMaximumScale);
export const roundWeight = (value: string) => roundTo(value, invoiceItemWeightScale);
export const roundVolume = (value: string) => roundTo(value, invoiceItemVolumeScale);
export function hasChanged(changed: string[], fields: string[]) { return changed.some(field => fields.includes(field)); }
export function normalizePriceCalculationMode(value?: string): InvoiceItemPriceCalculationMode {
  return value?.trim().toLowerCase() === "lineamountdriven" ? "LineAmountDriven" : "UnitPriceDriven";
}
export function normalizeInvoiceItemPrices(item: ApiInvoiceItemDto) {
  const quantity = decimal(item.quantity);
  if (item.priceCalculationMode === "LineAmountDriven") {
    item.totalPrice = roundMoney(item.totalPrice);
    item.unitPrice = quantity.compare(0) > 0 ? decimal(item.totalPrice).divide(quantity, 5).toString() : "0";
  } else {
    item.unitPrice = roundUnitPrice(item.unitPrice);
    item.totalPrice = quantity.multiply(item.unitPrice).round(2).normalize().toString();
  }
}

export function recalculateInvoiceItem(item: ApiInvoiceItemDto, changedFields: string[]): ApiInvoiceItemDto {
  const next = { ...item };
  const changed = (...fields: string[]) => hasChanged(changedFields, fields);
  const quantity = decimal(next.quantity);
  for (const field of ["gwPerCtn", "nwPerCtn", "gwTotal", "nwTotal"] as const) {
    if (changed(field)) next[field] = roundWeight(next[field]);
  }
  if (changed("volume")) next.volume = roundVolume(next.volume);
  next.priceCalculationMode = changed("totalPrice") ? "LineAmountDriven" : changed("unitPrice") ? "UnitPriceDriven" : normalizePriceCalculationMode(next.priceCalculationMode);
  if (changed("quantity", "pcsPerCtn") && decimal(next.pcsPerCtn).compare(0) > 0)
    next.cartons = quantity.divide(next.pcsPerCtn).ceiling().toString();
  const cartons = decimal(next.cartons);
  if (changed("quantity", "totalPrice", "unitPrice")) normalizeInvoiceItemPrices(next);
  if (changed("quantity", "purchasePrice")) next.purchaseTotal = quantity.multiply(decimal(next.purchasePrice)).round(2).normalize().toString();
  if (!changed("volume") && changed("quantity", "pcsPerCtn", "cartons", "length", "width", "height")) {
    next.volume = [next.length, next.width, next.height, next.cartons].every(value => decimal(value).compare(0) > 0)
      ? decimal(next.length).multiply(decimal(next.width)).multiply(decimal(next.height)).multiply(cartons).divide(1_000_000, 3).toString() : "0";
  }
  for (const [perCarton, total] of [["gwPerCtn", "gwTotal"], ["nwPerCtn", "nwTotal"]] as const) {
    if (!changed(total) && changed("quantity", "pcsPerCtn", "cartons", perCarton)) {
      next[total] = decimal(next[perCarton]).compare(0) > 0 && cartons.compare(0) > 0
        ? decimal(next[perCarton]).multiply(cartons).round(2).normalize().toString() : "0";
    }
  }
  return next;
}

type RowTotals = { amount: ExactDecimal; cartons: ExactDecimal; grossWeight: ExactDecimal; netWeight: ExactDecimal; purchase: ExactDecimal; quantity: ExactDecimal; refund: ExactDecimal; volume: ExactDecimal };
const totalInputFields = ["totalPrice", "cartons", "gwTotal", "nwTotal", "purchaseTotal", "quantity", "taxRebateRate", "volume"] as const;
export function haveInvoiceTotalsInputsChanged(previous: readonly ApiInvoiceItemDto[], next: readonly ApiInvoiceItemDto[]): boolean {
  return previous.length !== next.length || previous.some((item, index) => item !== next[index]
    && totalInputFields.some(field => item[field] !== next[index][field]));
}
const rowTotals = new WeakMap<ApiInvoiceItemDto, { fields: string[]; totals: RowTotals }>();
function itemTotals(item: ApiInvoiceItemDto): RowTotals {
  const fields = totalInputFields.map(field => decimalText(item[field]));
  const cached = rowTotals.get(item);
  if (cached && fields.every((value, index) => value === cached.fields[index])) return cached.totals;
  const [amount, cartons, grossWeight, netWeight, purchase, quantity, rate, volume] = fields.map(decimal);
  const totals = { amount, cartons, grossWeight, netWeight, purchase, quantity, volume, refund: purchase.divide("1.13").multiply(rate.divide(100)) };
  rowTotals.set(item, { fields, totals });
  return totals;
}

export function calculateInvoiceTotals(items: ApiInvoiceItemDto[], exchangeRate?: string | null, currency?: string): Partial<ApiInvoiceDetailDto> {
  const totals = { amount: decimal(), cartons: decimal(), grossWeight: decimal(), netWeight: decimal(), purchase: decimal(), quantity: decimal(), refund: decimal(), volume: decimal() };
  const keys = Object.keys(totals) as Array<keyof RowTotals>;
  for (const item of items) {
    const row = itemTotals(item);
    for (const key of keys) totals[key] = totals[key].add(row[key]);
  }
  const amount = totals.amount.round(2);
  const purchase = totals.purchase.round(2), refund = totals.refund.round(2);
  const selectedRate = decimal(exchangeRate);
  const rate = selectedRate.compare(0) > 0 ? selectedRate : decimal(["CNY", "RMB"].includes(normalizeText(currency).toUpperCase()) ? "1" : "0");
  const text = (value: ExactDecimal, digits = 2) => value.round(digits).normalize().toString();
  return { totalAmount: text(amount), totalCartons: text(totals.cartons), totalGrossWeight: text(totals.grossWeight),
    totalNetWeight: text(totals.netWeight), totalPurchaseAmount: text(purchase), totalQuantity: text(totals.quantity),
    totalTaxRefundAmount: text(refund), totalVolume: text(totals.volume, 3),
    totalProfit: rate.compare(0) > 0 ? text(amount.multiply(rate).subtract(purchase).add(refund)) : "0" };
}
