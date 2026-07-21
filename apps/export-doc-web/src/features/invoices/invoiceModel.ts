import { ApiInvoiceDetailDto } from "../../api/index.ts";
import { dateInputToApiDate, readNumber, toDateInputValue } from "../../ui/formUtils.ts";
import {
  createEmptyInvoiceItem,
  isMeaningfulInvoiceItem,
  normalizeInvoiceItemForSave,
} from "./InvoiceItemsEditor.tsx";

export const invoiceTypeOptions = [
  { value: "实际数据", label: "实际数据" },
  { value: "报关数据", label: "报关数据" },
];

export const invoiceStatusOptions = [
  { value: "Draft", label: "草稿" },
  { value: "Verified", label: "已核对" },
  { value: "Shipped", label: "已出运" },
  { value: "Completed", label: "已结汇" },
  { value: "Cancelled", label: "已作废" },
];

const lockedInvoiceStatuses = new Set(["Verified", "Shipped", "Completed", "Cancelled"]);

export function normalizeInvoiceType(value?: string) {
  const normalized = value?.trim() ?? "";
  return invoiceTypeOptions.some((option) => option.value === normalized) ? normalized : "实际数据";
}

export function getCounterpartInvoiceType(value?: string) {
  return normalizeInvoiceType(value) === "实际数据" ? "报关数据" : "实际数据";
}

export function normalizeInvoiceStatus(value?: string) {
  const normalized = value?.trim() ?? "";
  return normalized || "Draft";
}

export function canUnverifyInvoiceStatus(value?: string) {
  const normalized = normalizeInvoiceStatus(value);
  return lockedInvoiceStatuses.has(normalized);
}

export function isInvoiceEditableStatus(value?: string) {
  return !canUnverifyInvoiceStatus(value);
}

export function getInvoiceStatusOptions(value?: string) {
  const normalized = normalizeInvoiceStatus(value);
  return invoiceStatusOptions.some((option) => option.value === normalized)
    ? invoiceStatusOptions
    : [...invoiceStatusOptions, { value: normalized, label: normalized }];
}

export function getInvoiceStatusLabel(value?: string) {
  const normalized = normalizeInvoiceStatus(value);
  return invoiceStatusOptions.find((option) => option.value === normalized)?.label ?? value?.trim() ?? "-";
}

export function createEmptyInvoice(): ApiInvoiceDetailDto {
  const today = dateInputToApiDate(new Date().toISOString().slice(0, 10));

  return {
    id: 0,
    invoiceNo: "",
    contractNo: "",
    invoiceDate: today,
    shipmentDate: today,
    customerNameEN: "",
    exporterNameEN: "",
    customsBrokerName: "",
    customsBrokerCode: "",
    spare1: "",
    spare2: "",
    spare3: "",
    customFieldsJson: "",
    currency: "USD",
    supervisionMode: "一般贸易",
    status: "Draft",
    type: "实际数据",
    totalAmount: 0,
    totalCartons: 0,
    totalQuantity: 0,
    totalGrossWeight: 0,
    totalNetWeight: 0,
    totalVolume: 0,
    totalPurchaseAmount: 0,
    totalTaxRefundAmount: 0,
    totalProfit: 0,
    exchangeRate: 0,
    shippingMarksType: "Text",
    specialTerms: "",
    letterOfCreditNo: "",
    letterOfCreditSourcePath: "",
    letterOfCreditContent: "",
    items: [],
  };
}

export function readRouteInvoiceDraft(state: unknown): ApiInvoiceDetailDto | null {
  if (!state || typeof state !== "object" || !("invoiceDraft" in state)) {
    return null;
  }

  const draft = (state as { invoiceDraft?: unknown }).invoiceDraft;
  if (!draft || typeof draft !== "object") {
    return null;
  }

  const invoiceDraft = draft as ApiInvoiceDetailDto;
  return uppercaseInvoiceEnglishText({
    ...createEmptyInvoice(),
    ...invoiceDraft,
    id: 0,
    rowVersion: "",
    items: (invoiceDraft.items ?? []).map((item) => ({
      ...createEmptyInvoiceItem(0),
      ...item,
      id: 0,
      invoiceId: 0,
    })),
  });
}

function uppercaseAsciiText(value?: string) {
  return value?.replace(/[a-z]/g, (character) => character.toUpperCase()) ?? "";
}

export function uppercaseInvoiceEnglishText(invoice: ApiInvoiceDetailDto): ApiInvoiceDetailDto {
  return {
    ...invoice,
    invoiceNo: uppercaseAsciiText(invoice.invoiceNo),
    contractNo: uppercaseAsciiText(invoice.contractNo),
    customerNameEN: uppercaseAsciiText(invoice.customerNameEN),
    customerAddressEN: uppercaseAsciiText(invoice.customerAddressEN),
    notifyPartyName: uppercaseAsciiText(invoice.notifyPartyName),
    notifyPartyAddress: uppercaseAsciiText(invoice.notifyPartyAddress),
    exporterNameEN: uppercaseAsciiText(invoice.exporterNameEN),
    exporterNameCN: uppercaseAsciiText(invoice.exporterNameCN),
    exporterAddressEN: uppercaseAsciiText(invoice.exporterAddressEN),
    exporterAddressCN: uppercaseAsciiText(invoice.exporterAddressCN),
    exporterCreditCode: uppercaseAsciiText(invoice.exporterCreditCode),
    exporterCustomsCode: uppercaseAsciiText(invoice.exporterCustomsCode),
    customsBrokerName: uppercaseAsciiText(invoice.customsBrokerName),
    customsBrokerCode: uppercaseAsciiText(invoice.customsBrokerCode),
    paymentTerms: uppercaseAsciiText(invoice.paymentTerms),
    portOfLoading: uppercaseAsciiText(invoice.portOfLoading),
    portOfDestination: uppercaseAsciiText(invoice.portOfDestination),
    destinationCountry: uppercaseAsciiText(invoice.destinationCountry),
    tradeTerms: uppercaseAsciiText(invoice.tradeTerms),
    transportMode: uppercaseAsciiText(invoice.transportMode),
    currency: uppercaseAsciiText(invoice.currency),
    supervisionMode: uppercaseAsciiText(invoice.supervisionMode),
    issuingBank: uppercaseAsciiText(invoice.issuingBank),
    bankName: uppercaseAsciiText(invoice.bankName),
    bankAccount: uppercaseAsciiText(invoice.bankAccount),
    swiftCode: uppercaseAsciiText(invoice.swiftCode),
    shippingMarks: uppercaseAsciiText(invoice.shippingMarks),
    specialTerms: uppercaseAsciiText(invoice.specialTerms),
    letterOfCreditNo: uppercaseAsciiText(invoice.letterOfCreditNo),
    spare1: uppercaseAsciiText(invoice.spare1),
    spare2: uppercaseAsciiText(invoice.spare2),
    spare3: uppercaseAsciiText(invoice.spare3),
    items: (invoice.items ?? []).map((item) => ({
      ...item,
      poNumber: uppercaseAsciiText(item.poNumber),
      styleNo: uppercaseAsciiText(item.styleNo),
      styleName: uppercaseAsciiText(item.styleName),
      styleNameCN: uppercaseAsciiText(item.styleNameCN),
      fabricComposition: uppercaseAsciiText(item.fabricComposition),
      brand: uppercaseAsciiText(item.brand),
      hsCode: uppercaseAsciiText(item.hsCode),
      origin: uppercaseAsciiText(item.origin),
      unitEN: uppercaseAsciiText(item.unitEN),
      unitCN: uppercaseAsciiText(item.unitCN),
      ctnUnitEN: uppercaseAsciiText(item.ctnUnitEN),
      ctnUnitCN: uppercaseAsciiText(item.ctnUnitCN),
      spare1: uppercaseAsciiText(item.spare1),
      spare2: uppercaseAsciiText(item.spare2),
      spare3: uppercaseAsciiText(item.spare3),
    })),
  };
}

export type RouteInvoiceImportAction = "Overwrite" | "AppendItems";

export function readRouteInvoiceImportAction(state: unknown): RouteInvoiceImportAction | null {
  if (!state || typeof state !== "object" || !("invoiceImportAction" in state)) {
    return null;
  }

  const action = (state as { invoiceImportAction?: unknown }).invoiceImportAction;
  return action === "Overwrite" || action === "AppendItems" ? action : null;
}

export function normalizeInvoiceForSave(invoice: ApiInvoiceDetailDto, id: number): ApiInvoiceDetailDto {
  const items = (invoice.items ?? []).map(normalizeInvoiceItemForSave).filter(isMeaningfulInvoiceItem);

  return {
    ...invoice,
    id,
    invoiceNo: invoice.invoiceNo.trim(),
    contractNo: invoice.contractNo?.trim() ?? "",
    invoiceDate: dateInputToApiDate(toDateInputValue(invoice.invoiceDate)),
    shipmentDate: dateInputToApiDate(toDateInputValue(invoice.shipmentDate)),
    customsBrokerName: invoice.customsBrokerName?.trim() ?? "",
    customsBrokerCode: invoice.customsBrokerCode?.trim() ?? "",
    spare1: invoice.spare1?.trim() ?? "",
    spare2: invoice.spare2?.trim() ?? "",
    spare3: invoice.spare3?.trim() ?? "",
    customFieldsJson: invoice.customFieldsJson ?? "",
    paymentTerms: invoice.paymentTerms?.trim() ?? "",
    portOfLoading: invoice.portOfLoading?.trim() ?? "",
    portOfDestination: invoice.portOfDestination?.trim() ?? "",
    destinationCountry: invoice.destinationCountry?.trim() ?? "",
    tradeTerms: invoice.tradeTerms?.trim() ?? "",
    transportMode: invoice.transportMode?.trim() ?? "",
    customerNameEN: invoice.customerNameEN?.trim() ?? "",
    customerAddressEN: invoice.customerAddressEN?.trim() ?? "",
    notifyPartyName: invoice.notifyPartyName?.trim() ?? "",
    notifyPartyAddress: invoice.notifyPartyAddress?.trim() ?? "",
    exporterNameEN: invoice.exporterNameEN?.trim() ?? "",
    exporterNameCN: invoice.exporterNameCN?.trim() ?? "",
    exporterAddressEN: invoice.exporterAddressEN?.trim() ?? "",
    exporterAddressCN: invoice.exporterAddressCN?.trim() ?? "",
    exporterCreditCode: invoice.exporterCreditCode?.trim() ?? "",
    exporterCustomsCode: invoice.exporterCustomsCode?.trim() ?? "",
    bankName: invoice.bankName?.trim() ?? "",
    bankAccount: invoice.bankAccount?.trim() ?? "",
    swiftCode: invoice.swiftCode?.trim() ?? "",
    currency: invoice.currency?.trim() || "USD",
    supervisionMode: invoice.supervisionMode?.trim() || "一般贸易",
    status: normalizeInvoiceStatus(invoice.status),
    type: normalizeInvoiceType(invoice.type),
    totalAmount: readNumber(String(invoice.totalAmount)),
    totalCartons: readNumber(String(invoice.totalCartons)),
    totalQuantity: readNumber(String(invoice.totalQuantity)),
    totalGrossWeight: readNumber(String(invoice.totalGrossWeight)),
    totalNetWeight: readNumber(String(invoice.totalNetWeight)),
    totalVolume: readNumber(String(invoice.totalVolume)),
    totalPurchaseAmount: readNumber(String(invoice.totalPurchaseAmount)),
    totalTaxRefundAmount: readNumber(String(invoice.totalTaxRefundAmount)),
    totalProfit: readNumber(String(invoice.totalProfit)),
    exchangeRate: readNumber(String(invoice.exchangeRate)),
    shippingMarksType: invoice.shippingMarksType?.trim() || "Text",
    specialTerms: invoice.specialTerms ?? "",
    letterOfCreditNo: invoice.letterOfCreditNo?.trim() ?? "",
    letterOfCreditSourcePath: invoice.letterOfCreditSourcePath?.trim() ?? "",
    letterOfCreditContent: invoice.letterOfCreditContent ?? "",
    items,
  };
}
