import type { ApiInvoiceItemDto } from "../../api/index.ts";
import { normalizeText } from "../../ui/formUtils.ts";

export type InvoiceHsFeedbackContext = {
  productName: string;
  specification: string;
};

export function buildInvoiceHsQuery(item: ApiInvoiceItemDto | null) {
  if (!item) return "";
  const hsCodePrefix = normalizeText(item.hsCode).replace(/[\s.\-_/]/g, "");
  if (/^\d{4,}$/.test(hsCodePrefix)) return hsCodePrefix;
  return [item.styleNameCN, item.styleName, item.fabricComposition, item.brand]
    .map(normalizeText)
    .filter(Boolean)
    .join(" ");
}

export function buildInvoiceHsFeedbackContext(
  item: ApiInvoiceItemDto | null,
  fallbackName = "",
  fallbackSpecification = "",
): InvoiceHsFeedbackContext {
  const productName = normalizeText(item?.styleNameCN) || normalizeText(item?.styleName) || normalizeText(fallbackName);
  const specificationParts = [item?.styleName, item?.fabricComposition, item?.brand]
    .map(normalizeText)
    .filter((value) => value && value.localeCompare(productName, undefined, { sensitivity: "base" }) !== 0)
    .filter((value, index, values) => values.findIndex((candidate) =>
      candidate.localeCompare(value, undefined, { sensitivity: "base" }) === 0) === index);

  return {
    productName,
    specification: specificationParts.join(" · ") || normalizeText(fallbackSpecification),
  };
}
