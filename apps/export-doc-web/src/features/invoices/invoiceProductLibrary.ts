import { ApiInvoiceItemDto, ApiProductDto } from "../../api/index.ts";
import { normalizeText } from "../../ui/formUtils.ts";
import { decimalText } from "../../ui/decimalSum.ts";
import { createEmptyInvoiceItem, normalizeInvoiceItemForSave, recalculateInvoiceItem } from "./invoiceItemsEditorModel.ts";

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
      length: decimalText(product.length),
      width: decimalText(product.width),
      height: decimalText(product.height),
      gwPerCtn: decimalText(product.gwPerCtn),
      nwPerCtn: decimalText(product.nwPerCtn),
      pcsPerCtn: decimalText(product.pcsPerCtn),
      ctnUnitEN: normalizeText(product.packageUnitEN),
      ctnUnitCN: normalizeText(product.packageUnitCN),
      unitPrice: decimalText(product.defaultPrice),
      taxRebateRate: decimalText(product.taxRebateRate),
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
    taxRebateRate: decimalText(normalized.taxRebateRate),
    material: normalizeText(normalized.fabricComposition),
    brand: normalizeText(normalized.brand),
    origin: normalizeText(normalized.origin),
    unitEN: normalizeText(normalized.unitEN),
    unitCN: normalizeText(normalized.unitCN),
    length: decimalText(normalized.length),
    width: decimalText(normalized.width),
    height: decimalText(normalized.height),
    gwPerCtn: decimalText(normalized.gwPerCtn),
    nwPerCtn: decimalText(normalized.nwPerCtn),
    pcsPerCtn: decimalText(normalized.pcsPerCtn),
    packageUnitEN: normalizeText(normalized.ctnUnitEN),
    packageUnitCN: normalizeText(normalized.ctnUnitCN),
    defaultPrice: decimalText(normalized.unitPrice),
    createdAt: existing?.createdAt ?? new Date(0).toISOString(),
    updatedAt: existing?.updatedAt ?? new Date(0).toISOString(),
    rowVersion: existing?.rowVersion ?? "",
  };
}
