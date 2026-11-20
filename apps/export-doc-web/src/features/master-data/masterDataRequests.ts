import type { ApiCustomerDto, ApiExporterDto, ApiPayeeDto, ApiProductDto, ApiPortDto, ApiUnitDto, ApiHsCodeDto } from "../../api/index.ts";
import type { MasterDataRecord } from "./masterDataTypes.ts";
import { readString, normalizeCustomerRecord, normalizeProductRecord, normalizeHsCodeRecord, normalizeTextFields,
  exporterTextFields, payeeTextFields, portTextFields, unitTextFields } from "./masterDataModel.ts";

// Required DTO fields are checked by TypeScript against the shared normalization rules.
export function customerRequest(record: MasterDataRecord): ApiCustomerDto {
  return normalizeCustomerRecord(record, record.id);
}
export function exporterRequest(record: MasterDataRecord): ApiExporterDto {
  return normalizeTextFields(record, record.id, exporterTextFields);
}
export function payeeRequest(record: MasterDataRecord): ApiPayeeDto {
  return { ...normalizeTextFields(record, record.id, payeeTextFields), rowVersion: readString(record, "rowVersion") };
}
export function productRequest(record: MasterDataRecord): ApiProductDto {
  const now = new Date().toISOString();
  return { ...normalizeProductRecord(record, record.id), rowVersion: readString(record, "rowVersion"),
    createdAt: readString(record, "createdAt") || now, updatedAt: readString(record, "updatedAt") || now };
}
export function portRequest(record: MasterDataRecord): ApiPortDto {
  return { ...normalizeTextFields(record, record.id, portTextFields), rowVersion: readString(record, "rowVersion") };
}
export function unitRequest(record: MasterDataRecord): ApiUnitDto {
  return { ...normalizeTextFields(record, record.id, unitTextFields), rowVersion: readString(record, "rowVersion") };
}
export function hsCodeRequest(record: MasterDataRecord): ApiHsCodeDto {
  const normalized = normalizeHsCodeRecord(record, record.id);
  return { ...normalized,
    ...normalizeTextFields(normalized, record.id, ["remoteRecordKind", "summaryUrl", "evidenceUrl", "personalPostalTaxCode", "rowVersion"]),
    declarationExampleCount: typeof record.declarationExampleCount === "number" ? record.declarationExampleCount : 0 };
}
