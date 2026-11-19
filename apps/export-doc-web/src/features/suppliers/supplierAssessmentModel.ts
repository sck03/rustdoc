import type { ApiSupplierAssessmentDto, ApiSupplierAssessmentSaveRequest } from "../../api/index.ts";
export const ASSESSMENT_FIELDS = ["assessmentDate","assessmentKind","qualityScore","deliveryScore","serviceScore","priceScore","conclusion","notes"] as const;
export function emptyAssessment(supplierCompanyId: number, today: string): ApiSupplierAssessmentDto {
  return { id: 0, supplierCompanyId, assessmentDate: today, assessmentKind: "定期评价", qualityScore: 4, deliveryScore: 4,
    serviceScore: 4, priceScore: 4, averageScore: 4, conclusion: "合格", notes: "", assessedBy: "", status: "Draft",
    confirmedBy: "", confirmedAt: null, createdAt: "", updatedAt: "", versionNumber: 0 };
}
export function assessmentRequest(record: ApiSupplierAssessmentDto, values: Record<typeof ASSESSMENT_FIELDS[number], string>): ApiSupplierAssessmentSaveRequest {
  return { id: record.id, supplierCompanyId: record.supplierCompanyId, expectedVersion: record.versionNumber,
    assessmentDate: values.assessmentDate, assessmentKind: values.assessmentKind, qualityScore: Number(values.qualityScore),
    deliveryScore: Number(values.deliveryScore), serviceScore: Number(values.serviceScore), priceScore: Number(values.priceScore),
    conclusion: values.conclusion, notes: values.notes.trim() };
}
