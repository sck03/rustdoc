import type { ApiSalesOpportunityDto, ApiSalesOpportunitySaveRequest } from "../../api/index.ts";

export const opportunityStages = ["线索", "需求确认", "已报价", "谈判中", "已成交", "已失单"];
export const opportunityFields = ["crmCustomerId", "productId", "title", "quotationNo", "estimatedAmount", "currency",
  "probabilityPercent", "expectedCloseDate", "nextAction", "notes", "changeNote"] as const;
export type OpportunityValues = Record<typeof opportunityFields[number], string>;
export type OpportunityRecord = ApiSalesOpportunityDto & { changeNote: string };
export const opportunityRecord = (record: ApiSalesOpportunityDto): OpportunityRecord => ({ ...record, changeNote: "" });
export const opportunityDraftKey = (id: number | null) => `opportunity:${id ?? "new"}`;
export const opportunityDetailKey = (id: number | null, scope: string) => ["sales-opportunities", "detail", scope, id] as const;
export function opportunityClosed(record?: ApiSalesOpportunityDto) {
  return Boolean(record?.isArchived || record?.stage === "已成交" || record?.stage === "已失单");
}
export function opportunityRequest(values: OpportunityValues, record: ApiSalesOpportunityDto | undefined, quoteAllowed: boolean): ApiSalesOpportunitySaveRequest {
  return {
    id: record?.id ?? 0, expectedVersion: record?.versionNumber ?? 0,
    crmCustomerId: Number(values.crmCustomerId), title: values.title.trim(),
    productId: quoteAllowed ? Number(values.productId) || null : record?.productId ?? null,
    quotationNo: quoteAllowed ? values.quotationNo.trim() : record?.quotationNo ?? "",
    estimatedAmount: quoteAllowed ? values.estimatedAmount.trim() || "0" : record?.estimatedAmount ?? "0",
    currency: quoteAllowed ? values.currency.trim().toUpperCase() || "USD" : record?.currency ?? "USD",
    probabilityPercent: quoteAllowed ? Number(values.probabilityPercent || 0) : record?.probabilityPercent ?? 0,
    expectedCloseDate: quoteAllowed ? values.expectedCloseDate || null : record?.expectedCloseDate ?? null,
    nextAction: values.nextAction.trim(), notes: values.notes.trim(), changeNote: values.changeNote.trim(),
  };
}
