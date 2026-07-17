import { ApiInvoiceItemDto, ApiProductDto } from "../../api/index.ts";
import { normalizeText, numberValue } from "../../ui/formUtils.ts";
import { createEmptyInvoiceItem, normalizeInvoiceItemForSave, recalculateInvoiceItem } from "./InvoiceItemsEditor.tsx";

const productMappedItemFields = [
  "styleNo",
  "styleName",
  "styleNameCN",
  "fabricComposition",
  "brand",
  "hsCode",
  "origin",
  "unitEN",
  "unitCN",
  "length",
  "width",
  "height",
  "gwPerCtn",
  "nwPerCtn",
  "pcsPerCtn",
  "ctnUnitEN",
  "ctnUnitCN",
  "unitPrice",
  "taxRebateRate",
];

export function createInvoiceItemFromProduct(product: ApiProductDto, invoiceId: number): ApiInvoiceItemDto {
  return recalculateInvoiceItem(
    {
      ...createEmptyInvoiceItem(invoiceId),
      styleNo: normalizeText(product.productCode),
      styleName: normalizeText(product.nameEN),
      styleNameCN: normalizeText(product.nameCN),
      hsCode: normalizeText(product.hsCode),
      fabricComposition: normalizeText(product.material),
      brand: normalizeText(product.brand),
      origin: normalizeText(product.origin),
      unitEN: normalizeText(product.unitEN),
      unitCN: normalizeText(product.unitCN),
      length: numberValue(product.length),
      width: numberValue(product.width),
      height: numberValue(product.height),
      gwPerCtn: numberValue(product.gwPerCtn),
      nwPerCtn: numberValue(product.nwPerCtn),
      pcsPerCtn: numberValue(product.pcsPerCtn),
      ctnUnitEN: normalizeText(product.packageUnitEN),
      ctnUnitCN: normalizeText(product.packageUnitCN),
      unitPrice: numberValue(product.defaultPrice),
      taxRebateRate: numberValue(product.taxRebateRate),
    },
    productMappedItemFields,
  );
}

export function createProductDraftFromInvoiceItem(item: ApiInvoiceItemDto, existing?: ApiProductDto | null): ApiProductDto {
  const normalized = normalizeInvoiceItemForSave(item);
  return {
    id: existing?.id ?? 0,
    productCode: normalizeText(normalized.styleNo),
    nameEN: normalizeText(normalized.styleName),
    nameCN: normalizeText(normalized.styleNameCN),
    description: normalizeText(existing?.description),
    hsCode: normalizeText(normalized.hsCode),
    elements: normalizeText(existing?.elements),
    supervisionConditions: normalizeText(existing?.supervisionConditions),
    inspectionCategory: normalizeText(existing?.inspectionCategory),
    taxRebateRate: numberValue(normalized.taxRebateRate),
    material: normalizeText(normalized.fabricComposition),
    brand: normalizeText(normalized.brand),
    origin: normalizeText(normalized.origin),
    unitEN: normalizeText(normalized.unitEN),
    unitCN: normalizeText(normalized.unitCN),
    length: numberValue(normalized.length),
    width: numberValue(normalized.width),
    height: numberValue(normalized.height),
    gwPerCtn: numberValue(normalized.gwPerCtn),
    nwPerCtn: numberValue(normalized.nwPerCtn),
    pcsPerCtn: numberValue(normalized.pcsPerCtn),
    packageUnitEN: normalizeText(normalized.ctnUnitEN),
    packageUnitCN: normalizeText(normalized.ctnUnitCN),
    defaultPrice: numberValue(normalized.unitPrice),
    createdAt: existing?.createdAt,
    updatedAt: existing?.updatedAt,
  };
}

export function formatProductLibraryOption(product: ApiProductDto) {
  const code = normalizeText(product.productCode);
  const name = normalizeText(product.nameEN || product.nameCN);
  const hsCode = normalizeText(product.hsCode);
  return [code, name, hsCode].filter(Boolean).join(" / ") || `#${product.id}`;
}

export function hasSameProductCode(product: ApiProductDto, productCode: string) {
  return normalizeText(product.productCode).toUpperCase() === normalizeText(productCode).toUpperCase();
}
