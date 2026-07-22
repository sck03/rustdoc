import type { ApiAgentConsignmentDocumentDto, SingleWindowReferenceCatalogModel } from "../../api/index.ts";
import { formatPlainNumber } from "../../ui/formUtils.ts";

export type AgentScopedClearRequest = {
  snapshot: ApiAgentConsignmentDocumentDto;
  groupKey: string;
  categoryKey?: string;
  categoryLabel?: string;
};

export function buildAgentConsignmentSectionNavItems(document: ApiAgentConsignmentDocumentDto) {
  return [
    { id: "acd-section-status", label: "草稿", badge: `${document.warningCount} 预警` },
    { id: "acd-section-basic", label: "报文申报" },
    { id: "acd-section-documents", label: "单证费用" },
    { id: "acd-section-receipt", label: "回执" },
    { id: "acd-section-review", label: "预检" },
  ];
}

export function buildAgentConsignmentEditorOptions(catalog?: SingleWindowReferenceCatalogModel) {
  return {
    tradeModeOptions: (catalog?.acdTradeModes ?? [])
      .filter((item) => item.code?.trim() && item.name?.trim())
      .sort((left, right) => left.code.localeCompare(right.code, "zh-CN"))
      .map((item) => ({
        value: item.code.trim(),
        label: item.description?.trim()
          ? `${item.code.trim()}：${item.name.trim()} - ${item.description.trim()}`
          : `${item.code.trim()}：${item.name.trim()}`,
      })),
    countryOptions: (catalog?.acdCountries ?? [])
      .filter((item) => item.code?.trim() && item.chineseName?.trim())
      .sort((left, right) => left.code.localeCompare(right.code, "zh-CN"))
      .map((item) => ({
        value: item.code.trim(),
        label: `${item.code.trim()}：${item.chineseName.trim()}${item.englishName?.trim() ? ` / ${item.englishName.trim()}` : ""}`,
      })),
    currencyOptions: (catalog?.currencies ?? [])
      .filter((item) => item.acdCode?.trim() && item.alphaCode?.trim())
      .sort((left, right) => left.acdCode.localeCompare(right.acdCode, "zh-CN"))
      .map((item) => ({
        value: item.acdCode.trim(),
        label: `${item.acdCode.trim()}：${item.alphaCode.trim()}${item.code?.trim() ? ` / ${item.code.trim()}` : ""}`,
      })),
  };
}

export function normalizeAgentConsignmentDocumentForSave(document: ApiAgentConsignmentDocumentDto, invoiceId: number): ApiAgentConsignmentDocumentDto {
  return { ...document, id: numberOrZero(document.id), sourceInvoiceId: invoiceId };
}

export function buildAgentConsignmentDocumentSnapshot(document: ApiAgentConsignmentDocumentDto, invoiceId: number) {
  return JSON.stringify(normalizeAgentConsignmentDocumentForSave(document, invoiceId));
}

export function formatScopedClearResultMessage(request: AgentScopedClearRequest, changedCount: number) {
  if (request.categoryKey && request.categoryLabel) {
    return changedCount > 0
      ? `已把“${request.groupKey}”里的“${request.categoryLabel}”恢复到当前发票建议值，保存后写入草稿。`
      : `“${request.groupKey}”里的“${request.categoryLabel}”当前已经是建议值，无需恢复。`;
  }
  return changedCount > 0
    ? `已把“${request.groupKey}”分组恢复到当前发票建议值，保存后写入草稿。`
    : `“${request.groupKey}”分组当前已经是建议值，无需恢复。`;
}

export function formatAgentDateTime(value?: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("zh-CN", { hour12: false });
}

export function readAgentDisplayText(value?: string) {
  return value?.trim() ? value : "-";
}

export function readAgentDisplayValue(value?: string | number) {
  return typeof value === "number" ? formatPlainNumber(value) : readAgentDisplayText(value);
}

function numberOrZero(value?: number) {
  return Number.isFinite(value) ? Number(value) : 0;
}
