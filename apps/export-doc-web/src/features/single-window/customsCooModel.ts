import type {
  ApiCustomsCooAttachmentDto,
  ApiCustomsCooDocumentDto,
  ApiCustomsCooEditorOptionsResponse,
  ApiCustomsCooItemDto,
  ApiCustomsCooNonpartyCorpDto,
  ApiCustomsCooOptionDto,
  ApiCustomsCooProducerProfileInputDto,
  ApiSingleWindowIssuingAuthorityOptionDto,
} from "../../api/index.ts";
import { formatPlainNumber } from "../../ui/formUtils.ts";

const emptyCooOptionList: ApiCustomsCooOptionDto[] = [];

const cooHeaderProducerCertTypes = new Set(["A", "GE", "N", "L", "R", "P", "F", "K", "EC", "SE", "MV"]);
const cooHeaderRemarkCertTypes = new Set(["A", "GE", "N", "L", "R", "P", "F", "K", "SE", "MV"]);
const cooPredictFlagCertTypes = new Set(["H", "NI", "EC", "HD", "MV", "CG"]);
const cooOriginCountryCertTypes = new Set(["TR", "PR"]);
const cooGoodsOriginCriteriaHiddenCertTypes = new Set(["C", "AD", "CA", "NI", "SE", "HD"]);
const cooOriginCriteriaRefCertTypes = new Set(["E", "G"]);

export type CooConditionalHeaderField =
  | "Remark" | "Producer" | "ExhibitFlag" | "ThirdPartyInvFlag" | "OriCountryCode" | "OriCountry"
  | "PrcsAssembly" | "PredictFlag" | "ExporterTel" | "ExporterFax" | "ExporterEmail"
  | "ConsigneeTel" | "ConsigneeFax" | "ConsigneeEmail" | "EtpsConcEr" | "EtpsTel";

export type CooScopedClearRequest = {
  snapshot: ApiCustomsCooDocumentDto;
  groupKey: string;
  categoryKey?: string;
  categoryLabel?: string;
};

export function shouldShowCooModificationFields(document: ApiCustomsCooDocumentDto) {
  const certStatus = document.certStatus?.trim() ?? "";
  return certStatus === "1" || certStatus === "2" || certStatus === "3";
}

export function shouldShowCooNonpartyCorps(document: ApiCustomsCooDocumentDto) {
  return normalizeUpperText(document.certType) === "H" && normalizeText(document.thirdPartyInvFlag) === "1";
}

export function shouldShowCooHeaderField(document: ApiCustomsCooDocumentDto, field: CooConditionalHeaderField) {
  const certType = normalizeUpperText(document.certType);
  switch (field) {
    case "Remark":
      return cooHeaderRemarkCertTypes.has(certType);
    case "Producer":
      return cooHeaderProducerCertTypes.has(certType);
    case "ExhibitFlag":
      return certType === "E";
    case "ThirdPartyInvFlag":
      return certType === "H";
    case "OriCountryCode":
    case "OriCountry":
      return cooOriginCountryCertTypes.has(certType);
    case "PrcsAssembly":
      return certType === "PR";
    case "PredictFlag":
      return cooPredictFlagCertTypes.has(certType);
    case "ExporterTel":
    case "ExporterFax":
    case "ExporterEmail":
    case "ConsigneeTel":
    case "ConsigneeFax":
    case "ConsigneeEmail":
    case "EtpsConcEr":
    case "EtpsTel":
      return certType === "H";
    default:
      return true;
  }
}

export function shouldShowCooGoodsOriginCriteria(certType: string | undefined) {
  const normalized = normalizeUpperText(certType);
  return Boolean(normalized) &&
    !cooGoodsOriginCriteriaHiddenCertTypes.has(normalized);
}

export function shouldShowCooGoodsOriginCriteriaSub(certType: string | undefined) {
  return normalizeUpperText(certType) === "E";
}

export function shouldShowCooGoodsOriginCriteriaRef(certType: string | undefined, originCriteria: string | undefined) {
  const normalizedCertType = normalizeUpperText(certType);
  const normalizedOriginCriteria = normalizeUpperText(originCriteria);
  if (normalizedCertType === "G") {
    return normalizedOriginCriteria === "W" || normalizedOriginCriteria === "Y";
  }

  return cooOriginCriteriaRefCertTypes.has(normalizedCertType);
}

export function shouldShowCooGoodsRcepFields(certType: string | undefined) {
  return normalizeUpperText(certType) === "RC";
}

export function shouldShowCooGoodsProducerDescription(certType: string | undefined) {
  const normalized = normalizeUpperText(certType);
  return normalized === "H" || normalized === "RC";
}

export function shouldShowCooGoodsProducerContactFields(certType: string | undefined) {
  return normalizeUpperText(certType) === "H";
}

export function normalizeCooDocumentForSave(document: ApiCustomsCooDocumentDto, invoiceId: number): ApiCustomsCooDocumentDto {
  return {
    ...document,
    id: numberOrZero(document.id),
    sourceInvoiceId: invoiceId,
    items: document.items.map(normalizeCooItem).filter(isMeaningfulCooItem),
    nonpartyCorps: document.nonpartyCorps.map(normalizeNonpartyCorp).filter(isMeaningfulNonpartyCorp),
    attachments: document.attachments.map(normalizeAttachment),
  };
}

export function buildCooDocumentSnapshot(document: ApiCustomsCooDocumentDto, invoiceId: number) {
  return JSON.stringify(normalizeCooDocumentForSave(document, invoiceId));
}

export function formatScopedClearResultMessage(request: CooScopedClearRequest, changedCount: number) {
  if (request.categoryKey && request.categoryLabel) {
    return changedCount > 0
      ? `已把“${request.groupKey}”里的“${request.categoryLabel}”恢复到当前发票建议值，保存后写入草稿。`
      : `“${request.groupKey}”里的“${request.categoryLabel}”当前已经是建议值，无需恢复。`;
  }

  return changedCount > 0
    ? `已把“${request.groupKey}”分组恢复到当前发票建议值，保存后写入草稿。`
    : `“${request.groupKey}”分组当前已经是建议值，无需恢复。`;
}

export function buildProducerProfileInputFromCooItem(
  item: ApiCustomsCooItemDto,
  document: ApiCustomsCooDocumentDto,
): ApiCustomsCooProducerProfileInputDto {
  return {
    ciqRegNo: normalizeText(item.ciqRegNo),
    prdcEtpsName: normalizeText(item.prdcEtpsName),
    prdcEtpsConcEr: normalizeText(item.prdcEtpsConcEr),
    prdcEtpsTel: normalizeText(item.prdcEtpsTel),
    producer: normalizeText(item.producer),
    producerTel: normalizeText(item.producerTel),
    producerFax: normalizeText(item.producerFax),
    producerEmail: normalizeText(item.producerEmail),
    producerSertFlag: normalizeText(item.producerSertFlag).toUpperCase(),
    lastInvoiceNo: normalizeText(item.invNo || document.invNo || document.invoiceNo),
    lastContractNo: normalizeText(document.contractNo),
    lastSourceStyleNo: normalizeText(item.sourceStyleNo),
  };
}

export function applyProducerProfileToCooItem(
  item: ApiCustomsCooItemDto,
  profile: ApiCustomsCooProducerProfileInputDto,
): ApiCustomsCooItemDto {
  return {
    ...item,
    ciqRegNo: normalizeText(profile.ciqRegNo).toUpperCase(),
    prdcEtpsName: normalizeText(profile.prdcEtpsName),
    prdcEtpsConcEr: normalizeText(profile.prdcEtpsConcEr),
    prdcEtpsTel: normalizeText(profile.prdcEtpsTel),
    producer: normalizeText(profile.producer),
    producerTel: normalizeText(profile.producerTel),
    producerFax: normalizeText(profile.producerFax),
    producerEmail: normalizeText(profile.producerEmail),
    producerSertFlag: normalizeText(profile.producerSertFlag).toUpperCase(),
  };
}

export function countProducerProfileChanges(before: ApiCustomsCooItemDto, after: ApiCustomsCooItemDto) {
  const keys: Array<keyof ApiCustomsCooItemDto> = [
    "ciqRegNo",
    "prdcEtpsName",
    "prdcEtpsConcEr",
    "prdcEtpsTel",
    "producer",
    "producerTel",
    "producerFax",
    "producerEmail",
    "producerSertFlag",
  ];

  return keys.filter((key) => normalizeText(String(before[key] ?? "")) !== normalizeText(String(after[key] ?? ""))).length;
}

export function buildProducerProfileRowLabel(item: ApiCustomsCooItemDto, index: number) {
  const parts = [
    `第 ${index + 1} 行`,
    item.sourceStyleNo?.trim(),
    item.goodsNameE?.trim() || item.goodsName?.trim(),
  ].filter(Boolean);
  return parts.join(" · ");
}

const cooOriginAndEnterpriseCopyFields = [
  "oriCriteria",
  "oriCriteriaRef",
  "oriCriteriaSub",
  "ciqRegNo",
  "prdcEtpsName",
  "prdcEtpsConcEr",
  "prdcEtpsTel",
] as const;

type CooOriginAndEnterpriseCopyField = (typeof cooOriginAndEnterpriseCopyFields)[number];

export function copyCooOriginAndEnterpriseFields(source: ApiCustomsCooItemDto, target: ApiCustomsCooItemDto) {
  let nextItem = target;
  let changed = false;

  for (const field of cooOriginAndEnterpriseCopyFields) {
    const sourceValue = normalizeText(source[field]);
    const targetValue = normalizeText(target[field]);
    if (!sourceValue || sourceValue === targetValue) {
      continue;
    }

    if (nextItem === target) {
      nextItem = { ...target };
    }

    (nextItem as Record<CooOriginAndEnterpriseCopyField, string>)[field] = sourceValue;
    changed = true;
  }

  return { item: nextItem, changed };
}

export function getCooGoodsDescriptionActionTitle(item: ApiCustomsCooItemDto) {
  return isCooNonGoodsItem(item.goodsItemFlag) || isCooIrregularPack(item.packType)
    ? "按包装件数、包装单位/形式和英文性质/名称生成货物描述"
    : "按包装件数、包装单位和英文品名生成货物描述";
}

export function buildCooGoodsDescription(item: ApiCustomsCooItemDto) {
  const goodsName = firstNonEmpty(normalizeText(item.goodsNameE).toUpperCase(), normalizeText(item.goodsName).toUpperCase());
  if (!goodsName) {
    return "";
  }

  const packingSummary = buildCooPackingSummary(item.packQty, item.packUnit);
  return packingSummary ? `${packingSummary} OF ${goodsName}`.trim() : "";
}

export function buildCooPackingSummary(packQty: string, packUnit: string) {
  const normalizedPackQty = normalizeText(packQty);
  const unitText = resolveCooGoodsDescriptionPackUnit(packUnit, normalizedPackQty);
  if (!normalizedPackQty || !unitText) {
    return "";
  }

  const quantityText = buildCooGoodsDescriptionQuantityText(normalizedPackQty);
  return quantityText ? `${quantityText} ${unitText}`.trim() : `${normalizedPackQty} ${unitText}`.trim();
}

export function buildCooGoodsDescriptionQuantityText(packQty: string) {
  const quantity = parseStrictDecimal(packQty);
  if (quantity <= 0) {
    return "";
  }

  if (Number.isInteger(quantity) && quantity <= 999999999) {
    return `${toEnglishWords(quantity)} (${quantity})`;
  }

  return normalizeText(packQty);
}

export function resolveCooGoodsDescriptionPackUnit(packUnit: string, packQty: string) {
  const normalizedUnit = normalizeCooEnglishUnit(packUnit);
  const quantity = parseStrictDecimal(packQty);
  const singular = quantity === 1;

  switch (normalizedUnit) {
    case "CTN":
      return singular ? "CARTON" : "CARTONS";
    case "PCS":
      return singular ? "PIECE" : "PIECES";
    case "SET":
      return singular ? "SET" : "SETS";
    case "BOX":
      return singular ? "BOX" : "BOXES";
    case "KGS":
      return singular ? "KILOGRAM" : "KILOGRAMS";
    default:
      return normalizeText(packUnit).toUpperCase();
  }
}

export function normalizeCooEnglishUnit(value: string) {
  const key = normalizeText(value)
    .toUpperCase()
    .replace(/[^A-Z0-9]/g, "");
  const lookup: Record<string, string> = {
    BOX: "BOX",
    BOXES: "BOX",
    CARTON: "CTN",
    CARTONS: "CTN",
    CTN: "CTN",
    CTNS: "CTN",
    EA: "PCS",
    KGS: "KGS",
    KG: "KGS",
    KILOGRAM: "KGS",
    KILOGRAMS: "KGS",
    PCS: "PCS",
    PIECE: "PCS",
    PIECES: "PCS",
    SET: "SET",
    SETS: "SET",
  };

  return lookup[key] ?? normalizeText(value).toUpperCase();
}

export function getCooGoodsDescriptionFailureMessage(item: ApiCustomsCooItemDto) {
  return isCooNonGoodsItem(item.goodsItemFlag) || isCooIrregularPack(item.packType)
    ? "请先确认当前货项已经填了包装件数、包装单位/形式(英)和英文性质/名称，再点“生成货物描述”；挂装、散装、裸装可使用 HANGING GARMENT、IN BULK、PCS IN NUDE 等官方英文口径。"
    : "请先确认当前货项已经填了包装件数、包装单位(英)和英文品名，再点“生成货物描述”。";
}

export function isCooNonGoodsItem(value: string) {
  return normalizeText(value).toUpperCase() === "Y";
}

export function isCooIrregularPack(value: string) {
  return normalizeText(value) === "2";
}

export function parseStrictDecimal(value: string) {
  const trimmed = normalizeText(value);
  if (!/^[+-]?\d+(?:\.\d+)?$/.test(trimmed)) {
    return 0;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : 0;
}

export function toEnglishWords(value: number): string {
  if (value < 0) {
    return `MINUS ${toEnglishWords(Math.abs(value))}`;
  }

  if (value === 0) {
    return "ZERO";
  }

  return convertWholeNumberToEnglish(Math.trunc(value)).trim();
}

export function convertWholeNumberToEnglish(value: number): string {
  const ones = [
    "",
    "ONE",
    "TWO",
    "THREE",
    "FOUR",
    "FIVE",
    "SIX",
    "SEVEN",
    "EIGHT",
    "NINE",
    "TEN",
    "ELEVEN",
    "TWELVE",
    "THIRTEEN",
    "FOURTEEN",
    "FIFTEEN",
    "SIXTEEN",
    "SEVENTEEN",
    "EIGHTEEN",
    "NINETEEN",
  ];
  const tens = ["", "", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY"];

  if (value === 0) {
    return "";
  }

  if (value < 20) {
    return ones[value];
  }

  if (value < 100) {
    return `${tens[Math.trunc(value / 10)]}${value % 10 > 0 ? `-${ones[value % 10]}` : ""}`;
  }

  if (value < 1000) {
    return `${ones[Math.trunc(value / 100)]} HUNDRED${value % 100 > 0 ? ` AND ${convertWholeNumberToEnglish(value % 100)}` : ""}`;
  }

  if (value < 1000000) {
    return `${convertWholeNumberToEnglish(Math.trunc(value / 1000))} THOUSAND${value % 1000 > 0 ? ` ${convertWholeNumberToEnglish(value % 1000)}` : ""}`;
  }

  if (value < 1000000000) {
    return `${convertWholeNumberToEnglish(Math.trunc(value / 1000000))} MILLION${value % 1000000 > 0 ? ` ${convertWholeNumberToEnglish(value % 1000000)}` : ""}`;
  }

  return `${convertWholeNumberToEnglish(Math.trunc(value / 1000000000))} BILLION${value % 1000000000 > 0 ? ` ${convertWholeNumberToEnglish(value % 1000000000)}` : ""}`;
}

export function firstNonEmpty(...values: string[]) {
  return values.find((value) => value.trim())?.trim() ?? "";
}

export function normalizeCooItem(item: ApiCustomsCooItemDto, index: number): ApiCustomsCooItemDto {
  return {
    ...createEmptyCooItem(item.documentId, index + 1, item.invNo),
    ...item,
    id: numberOrZero(item.id),
    documentId: numberOrZero(item.documentId),
    gNo: numberOrZero(item.gNo) || index + 1,
    sourceItemId: numberOrZero(item.sourceItemId),
  };
}

export function normalizeNonpartyCorp(corp: ApiCustomsCooNonpartyCorpDto, index: number): ApiCustomsCooNonpartyCorpDto {
  return {
    ...createEmptyNonpartyCorp(corp.documentId, index + 1),
    ...corp,
    id: numberOrZero(corp.id),
    documentId: numberOrZero(corp.documentId),
    sortNo: numberOrZero(corp.sortNo) || index + 1,
  };
}

export function normalizeAttachment(attachment: ApiCustomsCooAttachmentDto): ApiCustomsCooAttachmentDto {
  return {
    ...attachment,
    id: numberOrZero(attachment.id),
    documentId: numberOrZero(attachment.documentId),
    sortOrder: numberOrZero(attachment.sortOrder),
  };
}

export function isMeaningfulCooItem(item: ApiCustomsCooItemDto) {
  const textValues = [
    item.ciqRegNo,
    item.fobValue,
    item.goodsDesc,
    item.goodsName,
    item.goodsNameE,
    item.goodsOriginCountry,
    item.goodsOriginCountryEn,
    item.goodsQty,
    item.goodsQtyRef,
    item.goodsUnit,
    item.goodsUnitE,
    item.goodsUnitRef,
    item.grossWt,
    item.hsCode,
    item.iCompPrpr,
    item.invNo,
    item.invPrice,
    item.invValue,
    item.netWt,
    item.oriCriteria,
    item.oriCriteriaRef,
    item.oriCriteriaSub,
    item.packType,
    item.packQty,
    item.packUnit,
    item.prdcEtpsConcEr,
    item.prdcEtpsName,
    item.prdcEtpsTel,
    item.producer,
    item.producerEmail,
    item.producerFax,
    item.producerSertFlag,
    item.producerTel,
    item.secdGoodsQtyRef,
    item.secdGoodsUnitRef,
    item.sourceStyleNo,
    item.wtUnit,
  ];

  return item.id > 0 || textValues.some((value) => Boolean(value?.trim()));
}

export function isMeaningfulNonpartyCorp(corp: ApiCustomsCooNonpartyCorpDto) {
  return (
    corp.id > 0 ||
    Boolean(corp.entName.trim()) ||
    Boolean(corp.entAddr.trim()) ||
    Boolean(corp.entCountryCode.trim()) ||
    Boolean(corp.entCountryName.trim())
  );
}

export function createEmptyCooItem(documentId: number, gNo: number, invNo = ""): ApiCustomsCooItemDto {
  return {
    ciqRegNo: "",
    documentId,
    fobValue: "",
    gNo,
    goodsDesc: "",
    goodsItemFlag: "",
    goodsName: "",
    goodsNameE: "",
    goodsOriginCountry: "",
    goodsOriginCountryEn: "",
    goodsQty: "",
    goodsQtyRef: "",
    goodsTaxRate: "",
    goodsUnit: "",
    goodsUnitE: "",
    goodsUnitRef: "",
    grossWt: "",
    hsCode: "",
    iCompPrpr: "",
    id: 0,
    invNo,
    invPrice: "",
    invValue: "",
    netWt: "",
    oriCriteria: "",
    oriCriteriaRef: "",
    oriCriteriaSub: "",
    packQty: "",
    packType: "",
    packUnit: "",
    prdcEtpsConcEr: "",
    prdcEtpsName: "",
    prdcEtpsTel: "",
    producer: "",
    producerEmail: "",
    producerFax: "",
    producerSertFlag: "",
    producerTel: "",
    secdGoodsQtyRef: "",
    secdGoodsUnitRef: "",
    sourceItemId: 0,
    sourceStyleNo: "",
    wtUnit: "",
  };
}

export function createEmptyNonpartyCorp(documentId: number, sortNo: number): ApiCustomsCooNonpartyCorpDto {
  return {
    documentId,
    entAddr: "",
    entCountryCode: "",
    entCountryName: "",
    entName: "",
    id: 0,
    sortNo,
  };
}

export function createAttachmentFromPath(
  document: ApiCustomsCooDocumentDto,
  filePath: string,
  sortOrder: number,
): ApiCustomsCooAttachmentDto {
  const fileName = fileNameFromPath(filePath);
  return {
    aplRegNo: normalizeText(document.aplRegNo),
    certNo: normalizeText(document.certNo),
    certType: normalizeText(document.certType) || "C",
    ciqRegNo: normalizeText(document.ciqRegNo),
    description: fileName,
    docType: resolveDocType(fileName),
    documentId: numberOrZero(document.id),
    fileExistsAtBuild: false,
    fileName,
    filePath,
    fileType: resolveCooAttachmentFileType(fileName),
    id: 0,
    isDelay: false,
    mediaType: "application/octet-stream",
    sortOrder,
  };
}

export function fileNameFromPath(path: string) {
  return path.split(/[\\/]/).filter(Boolean).pop()?.trim() || path.trim();
}

export function resolveDocType(fileName: string) {
  const extension = fileName.split(".").pop();
  return extension && extension !== fileName ? extension.trim().toUpperCase() : "";
}

export function resolveCooAttachmentFileType(fileName: string) {
  const normalized = fileName.trim().toUpperCase();
  if (!normalized) {
    return "7";
  }

  if (normalized.includes("第三方") || normalized.includes("NONPARTY") || normalized.includes("THIRDPARTY")) {
    return "2";
  }

  if (normalized.includes("发票") || normalized.includes("INVOICE")) {
    return "1";
  }

  if (
    normalized.includes("提单") ||
    normalized.includes("运单") ||
    normalized.includes("运输") ||
    normalized.includes("BILL") ||
    normalized.includes("B/L") ||
    normalized.includes("TRANSPORT")
  ) {
    return "3";
  }

  if (normalized.includes("报关") || normalized.includes("报关单") || normalized.includes("DECLARATION")) {
    return "4";
  }

  if (normalized.includes("成本") || normalized.includes("COST")) {
    return "5";
  }

  if (normalized.includes("采购") || normalized.includes("PURCHASE")) {
    return "6";
  }

  if (normalized.includes("原证") || normalized.includes("原产证") || normalized.includes("CERTIFICATE")) {
    return "15";
  }

  return "7";
}

export function formatDateTime(value?: string) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("zh-CN", { hour12: false });
}

export function readDisplayText(value?: string) {
  return value?.trim() ? value : "-";
}

export function readDisplayValue(value?: string | number) {
  if (typeof value === "number") {
    return formatPlainNumber(value);
  }

  return readDisplayText(value);
}

export function normalizeCooOptions(options: ApiCustomsCooOptionDto[]) {
  const normalized: ApiCustomsCooOptionDto[] = [];
  const seen = new Set<string>();
  for (const option of options ?? []) {
    const value = normalizeText(option.value);
    const label = normalizeText(option.label) || value;
    const key = `${value}\u0000${label}`.toUpperCase();
    if (seen.has(key)) {
      continue;
    }

    normalized.push({ value, label });
    seen.add(key);
  }

  return normalized;
}

export function buildCooSelectOptions(options: ApiCustomsCooOptionDto[], currentValue?: string) {
  const normalizedOptions = normalizeCooOptions(options);
  const hasEmpty = normalizedOptions.some((option) => option.value === "");
  const withEmpty = hasEmpty ? normalizedOptions : [{ value: "", label: "未选择" }, ...normalizedOptions];
  const value = normalizeText(currentValue);
  if (!value || withEmpty.some((option) => option.value === value)) {
    return withEmpty;
  }

  return [...withEmpty, { value, label: `${value}：当前草稿值` }];
}

export function toIssuingAuthorityOptions(options: ApiSingleWindowIssuingAuthorityOptionDto[]): ApiCustomsCooOptionDto[] {
  return options.map((option) => ({
    value: option.code,
    label: option.label || option.code,
  }));
}

export function parseIssuingAuthorityCode(value: string, options: ApiSingleWindowIssuingAuthorityOptionDto[]) {
  const trimmed = value.trim();
  if (!trimmed) {
    return "";
  }

  const codeMatch = trimmed.match(/(?:^|\D)(\d{4})(?:\D|$)/);
  if (codeMatch) {
    return codeMatch[1];
  }

  const normalized = normalizeAuthorityLookupText(trimmed);
  const matched = options.find((option) => {
    const normalizedCode = normalizeAuthorityLookupText(option.code);
    const normalizedLabel = normalizeAuthorityLookupText(option.label);
    return normalizedCode === normalized || normalizedLabel === normalized || (normalized.length >= 2 && normalizedLabel.includes(normalized));
  });

  return matched?.code || trimmed;
}

export function findIssuingAuthority(code: string, options: ApiSingleWindowIssuingAuthorityOptionDto[]) {
  const normalizedCode = normalizeAuthorityLookupText(code);
  return options.find((option) => normalizeAuthorityLookupText(option.code) === normalizedCode) ?? null;
}

export function normalizeAuthorityLookupText(value: string) {
  return value
    .trim()
    .replace(/[\s:：]/g, "")
    .toUpperCase();
}

export function normalizeAuthorityCompareText(value: string) {
  return value.trim().toUpperCase();
}

export function getCooOriginCriteriaOptions(options: ApiCustomsCooEditorOptionsResponse, certType: string) {
  const normalizedCertType = normalizeText(certType).toUpperCase();
  return (
    options.originCriteriaOptionSets.find((set) => normalizeText(set.certType).toUpperCase() === normalizedCertType)?.options ??
    emptyCooOptionList
  );
}

export function getCooOriginCriteriaSubOptions(
  options: ApiCustomsCooEditorOptionsResponse,
  certType: string,
  originCriteria: string,
) {
  const normalizedCertType = normalizeText(certType).toUpperCase();
  const normalizedOriginCriteria = normalizeText(originCriteria).toUpperCase();
  return (
    options.originCriteriaSubOptionSets.find(
      (set) =>
        normalizeText(set.certType).toUpperCase() === normalizedCertType &&
        normalizeText(set.originCriteria).toUpperCase() === normalizedOriginCriteria,
    )?.options ?? emptyCooOptionList
  );
}

export function numberOrZero(value?: number) {
  return Number.isFinite(value) ? Number(value) : 0;
}

export function normalizeText(value?: string) {
  return value?.trim() ?? "";
}

export function normalizeUpperText(value?: string) {
  return normalizeText(value).toUpperCase();
}
