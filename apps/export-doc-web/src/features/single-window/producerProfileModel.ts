import type { ApiCustomsCooProducerProfileDto, ApiCustomsCooProducerProfileInputDto } from "../../api/index.ts";

export type ProducerProfileDraft = Required<ApiCustomsCooProducerProfileInputDto> & { id: number; versionNumber: number };

export function toDraft(profile: ApiCustomsCooProducerProfileInputDto | ApiCustomsCooProducerProfileDto): ProducerProfileDraft {
  return {
    id: "id" in profile ? profile.id : 0,
    versionNumber: "versionNumber" in profile ? profile.versionNumber : 0,
    ciqRegNo: profile.ciqRegNo ?? "",
    prdcEtpsName: profile.prdcEtpsName ?? "",
    prdcEtpsConcEr: profile.prdcEtpsConcEr ?? "",
    prdcEtpsTel: profile.prdcEtpsTel ?? "",
    producer: profile.producer ?? "",
    producerTel: profile.producerTel ?? "",
    producerFax: profile.producerFax ?? "",
    producerEmail: profile.producerEmail ?? "",
    producerSertFlag: profile.producerSertFlag ?? "",
    lastInvoiceNo: profile.lastInvoiceNo ?? "",
    lastContractNo: profile.lastContractNo ?? "",
    lastSourceStyleNo: profile.lastSourceStyleNo ?? "",
  };
}

export function normalizeProfileInput(profile: ApiCustomsCooProducerProfileInputDto): ApiCustomsCooProducerProfileInputDto {
  return {
    ciqRegNo: normalizeUpper(profile.ciqRegNo),
    prdcEtpsName: normalizeText(profile.prdcEtpsName),
    prdcEtpsConcEr: normalizeText(profile.prdcEtpsConcEr),
    prdcEtpsTel: normalizeText(profile.prdcEtpsTel),
    producer: normalizeText(profile.producer),
    producerTel: normalizeText(profile.producerTel),
    producerFax: normalizeText(profile.producerFax),
    producerEmail: normalizeText(profile.producerEmail),
    producerSertFlag: normalizeUpper(profile.producerSertFlag),
    lastInvoiceNo: normalizeText(profile.lastInvoiceNo),
    lastContractNo: normalizeText(profile.lastContractNo),
    lastSourceStyleNo: normalizeText(profile.lastSourceStyleNo),
  };
}

export function producerProfileDraftEquals(left: ProducerProfileDraft, right: ProducerProfileDraft) {
  return JSON.stringify(toComparableProducerProfileDraft(left)) === JSON.stringify(toComparableProducerProfileDraft(right));
}

function toComparableProducerProfileDraft(profile: ProducerProfileDraft) {
  return {
    id: profile.id || 0,
    ...normalizeProfileInput(profile),
  };
}

export function hasProducerIdentity(profile: ApiCustomsCooProducerProfileInputDto) {
  return Boolean(profile.ciqRegNo?.trim() || profile.prdcEtpsName?.trim());
}

export function buildProfileDisplayName(profile: ApiCustomsCooProducerProfileInputDto) {
  const code = profile.ciqRegNo?.trim() ?? "";
  const name = profile.prdcEtpsName?.trim() ?? "";
  return code && name ? `${code} - ${name}` : name || code || "未命名";
}

export function readProfileValue(value?: string) {
  return value?.trim() ? value : "-";
}

function normalizeText(value?: string) {
  return value?.trim() ?? "";
}

function normalizeUpper(value?: string) {
  return normalizeText(value).toUpperCase();
}
