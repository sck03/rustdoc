import type { ApiSupplierProductLinkDto, ApiSupplierProductLinkSaveRequest } from "../../api/index.ts";

export const PRODUCT_LINK_FIELDS = ["productId", "supplierProductCode", "referencePrice", "currency", "leadTimeDays"] as const;
export type ProductLinkValues = Record<typeof PRODUCT_LINK_FIELDS[number], string>;
export function emptyProductLink(supplierCompanyId: number): ApiSupplierProductLinkDto {
  return { id: 0, versionNumber: 0, supplierCompanyId, productId: 0, productCode: "", productNameCN: "",
    productNameEN: "", supplierProductCode: "", referencePrice: "0", currency: "CNY", leadTimeDays: 0, status: "供货中" };
}
export function productLinkRequest(record: ApiSupplierProductLinkDto, values: ProductLinkValues): ApiSupplierProductLinkSaveRequest {
  if (!/^[+-]?[0-9]+(?:\.[0-9]+)?$/.test(values.referencePrice.trim())) throw new Error("参考价必须是十进制数，可带正负号。");
  const productId = Number(values.productId), leadTimeDays = Number(values.leadTimeDays);
  if (!Number.isSafeInteger(productId) || productId <= 0) throw new Error("请选择产品。");
  if (!Number.isSafeInteger(leadTimeDays) || leadTimeDays < 0 || leadTimeDays > 3650) throw new Error("交期天数必须在 0 至 3650 之间。");
  return { id: record.id, supplierCompanyId: record.supplierCompanyId, expectedVersion: record.versionNumber,
    productId, leadTimeDays, supplierProductCode: values.supplierProductCode.trim(),
    referencePrice: values.referencePrice.trim(), currency: values.currency.trim().toUpperCase() };
}
