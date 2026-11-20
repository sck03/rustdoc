import type { ApiHsCodeDto,ApiHsCodeRemoteDetailResolutionResponse,ApiProductDto,ApiUnitDto } from "../../api/index.ts";
import { normalizeText,numberValue } from "../../ui/formUtils.ts";
import { ExactDecimal, decimalText } from "../../ui/decimalSum.ts";
import type {
MasterDataColumnDefinition,
MasterDataEntityConfig,
MasterDataRecord,
ProductAssistanceField,
ProductInputAssistance,
ProductUnitAssistance,
ProductUnitSourceField,
ProductUnitTargetField
} from "./masterDataTypes.ts";
import { emptyProductInputAssistance,productUnitLookupTargets } from "./masterDataTypes.ts";

export function buildMasterDataDisplayName(config: MasterDataEntityConfig, record: MasterDataRecord) {
  const candidates = [
    readString(record, config.primaryField),
    ...config.columns.map((column) => readString(record, column.name)),
  ];
  const displayName = candidates.map((value) => normalizeText(value)).find(Boolean);
  if (displayName) {
    return displayName;
  }

  const id = numberValue(record.id);
  return id > 0 ? `#${id}` : config.routeId(record);
}

export function formatColumnValue(column: MasterDataColumnDefinition, record: MasterDataRecord) {
  const value = record[column.name];
  if (column.format) {
    return column.format(value, record);
  }

  if (typeof value === "number") {
    return value === 0 ? "0" : String(value);
  }

  return readDisplayString(value);
}

export function readString(record: MasterDataRecord, name: string) {
  const value = record[name];
  return typeof value === "string" ? value : value == null ? "" : String(value);
}

export function normalizeHsCodeDtoForRequest(item: ApiHsCodeDto): ApiHsCodeDto {
  return {
    ...item,
    code: normalizeText(item.code),
    description: normalizeText(item.description),
    detailUrl: normalizeText(item.detailUrl),
    elements: normalizeText(item.elements),
    id: numberValue(item.id),
    inspectionCategory: normalizeText(item.inspectionCategory),
    name: normalizeText(item.name),
    normalizedCode: normalizeText(item.normalizedCode),
    rebateRate: normalizeText(item.rebateRate),
    supervisionConditions: normalizeText(item.supervisionConditions),
    unit: normalizeText(item.unit),
    updateTime: item.updateTime,
    remoteRecordKind: normalizeText(item.remoteRecordKind),
    instanceCount: item.instanceCount,
    summaryUrl: normalizeText(item.summaryUrl),
    evidenceUrl: normalizeText(item.evidenceUrl),
    observedAt: item.observedAt,
    recommendedKeywords: item.recommendedKeywords ?? [],
    personalPostalTaxCode: normalizeText(item.personalPostalTaxCode),
    ciqEntries: item.ciqEntries ?? [],
    classificationEntries: item.classificationEntries ?? [],
    declarationExampleCount: item.declarationExampleCount ?? 0,
  };
}

export function normalizeHsCodeDtoForSave(item: ApiHsCodeDto): ApiHsCodeDto {
  return {
    ...normalizeHsCodeDtoForRequest(item),
    id: 0,
    status: item.remoteRecordKind ? "ReferenceOnly" : item.status,
  };
}

export function replaceHsCodeResult(items: ApiHsCodeDto[], original: ApiHsCodeDto, next: ApiHsCodeDto) {
  const normalizedNext = normalizeHsCodeDtoForRequest({
    ...next,
    remoteRecordKind: original.remoteRecordKind,
    instanceCount: original.instanceCount,
    summaryUrl: original.summaryUrl,
    evidenceUrl: original.evidenceUrl,
    observedAt: original.observedAt,
    recommendedKeywords: original.recommendedKeywords,
    personalPostalTaxCode: original.personalPostalTaxCode,
    ciqEntries: original.ciqEntries,
    classificationEntries: original.classificationEntries,
    declarationExampleCount: original.declarationExampleCount,
  });
  return items.map((item) => (isSameHsCodeResult(item, original) ? normalizedNext : item));
}

export function applyHsCodeRemoteDetailResolution(
  items: ApiHsCodeDto[],
  original: ApiHsCodeDto,
  response: ApiHsCodeRemoteDetailResolutionResponse,
) {
  const removedItems = response.removedItems ?? [];
  let nextItems = items.filter(
    (item) => !removedItems.some((removed) => isSameHsCodeResult(item, removed)) && !isRemovedOriginal(item, original, removedItems),
  );

  for (const item of response.items ?? []) {
    nextItems = upsertHsCodeResult(nextItems, original, item);
  }

  return nextItems;
}

function upsertHsCodeResult(items: ApiHsCodeDto[], original: ApiHsCodeDto, next: ApiHsCodeDto) {
  const normalizedNext = normalizeHsCodeDtoForRequest(next);
  const nextIdentity = normalizeText(normalizedNext.code || normalizedNext.normalizedCode || normalizedNext.detailUrl);
  if (!nextIdentity) {
    return items;
  }

  const existingIndex = items.findIndex((item) => isSameHsCodeResult(item, normalizedNext) || isSameHsCodeResult(item, original));
  if (existingIndex < 0) {
    return [...items, normalizedNext];
  }

  return items.map((item, index) => (index === existingIndex ? normalizedNext : item));
}

function isRemovedOriginal(item: ApiHsCodeDto, original: ApiHsCodeDto, removedItems: ApiHsCodeDto[]) {
  return isSameHsCodeResult(item, original) && removedItems.some((removed) => isSameHsCodeResult(removed, original));
}

export function shouldFetchRemoteHsCodeDetail(item: ApiHsCodeDto) {
  return item.remoteRecordKind !== "DeclarationExample" &&
    Boolean(normalizeText(item.detailUrl)) &&
    !normalizeText(item.elements);
}

function isSameHsCodeResult(left: ApiHsCodeDto, right: ApiHsCodeDto) {
  const leftCode = normalizeText(left.code || left.normalizedCode).toLowerCase();
  const rightCode = normalizeText(right.code || right.normalizedCode).toLowerCase();
  if (leftCode && rightCode) {
    return leftCode === rightCode;
  }

  const leftUrl = normalizeText(left.detailUrl).toLowerCase();
  const rightUrl = normalizeText(right.detailUrl).toLowerCase();
  return Boolean(leftUrl && rightUrl && leftUrl === rightUrl);
}

export function buildHsCodeResultKey(item: ApiHsCodeDto, index: number) {
  return `${item.code || item.normalizedCode || item.detailUrl || "hs-code"}-${index}`;
}

export function isProductUnitSourceField(name: string): name is ProductUnitSourceField {
  return name === "unitEN" || name === "packageUnitEN";
}

export function isProductUnitTargetField(name: string): name is ProductUnitTargetField {
  return name === "unitCN" || name === "packageUnitCN";
}

export function isProductUnitField(name: string) {
  return isProductUnitSourceField(name) || isProductUnitTargetField(name);
}

export function readProductInputAssistanceOptions(name: ProductAssistanceField, assistance: ProductInputAssistance) {
  return assistance[name] ?? emptyProductInputAssistance[name];
}

export function readProductUnitFieldOptions(name: string, assistance: ProductUnitAssistance) {
  return isProductUnitSourceField(name) ? assistance.englishOptions : assistance.chineseOptions;
}

export function buildProductInputAssistance(products: ApiProductDto[], hsCodes: ApiHsCodeDto[] = []): ProductInputAssistance {
  const activeHsCodes = (hsCodes ?? []).filter((hsCode) => !hsCode.requiresAnnualReview && (!hsCode.status || hsCode.status === "Active"));
  return {
    brand: buildUniqueTextSuggestions((products ?? []).map((product) => product.brand)),
    hsCode: buildUniqueTextSuggestions([
      ...(products ?? []).map((product) => product.hsCode),
      ...activeHsCodes.flatMap((hsCode) => [hsCode.code, hsCode.normalizedCode]),
    ], true),
    material: buildUniqueTextSuggestions((products ?? []).map((product) => product.material)),
    nameCN: buildUniqueTextSuggestions((products ?? []).map((product) => product.nameCN)),
    nameEN: buildUniqueTextSuggestions((products ?? []).map((product) => product.nameEN)),
    origin: buildUniqueTextSuggestions((products ?? []).map((product) => product.origin)),
    productCode: buildUniqueTextSuggestions((products ?? []).map((product) => product.productCode)),
  };
}

export function buildProductHsCodeLookup(hsCodes: ApiHsCodeDto[]) {
  const lookup = new Map<string, ApiHsCodeDto>();
  for (const hsCode of hsCodes ?? []) {
    if (hsCode.requiresAnnualReview || hsCode.status && hsCode.status !== "Active") {
      continue;
    }
    const key = normalizeHsCodeKey(hsCode.code || hsCode.normalizedCode);
    if (key && !lookup.has(key)) {
      lookup.set(key, hsCode);
    }
  }

  return lookup;
}

export function applyHsCodeToProductRecord(record: MasterDataRecord, hsCode: ApiHsCodeDto) {
  let next: MasterDataRecord = { ...record };
  const appliedLabels: string[] = [];

  const hsCodeValue = normalizeText(hsCode.code || hsCode.normalizedCode).toUpperCase();
  if (hsCodeValue && readString(next, "hsCode") !== hsCodeValue) {
    next = { ...next, hsCode: hsCodeValue };
    appliedLabels.push("HS 编码");
  }

  next = fillProductTextFieldFromHsCode(next, "description", "描述", hsCode.description || hsCode.name, appliedLabels);
  next = fillProductTextFieldFromHsCode(next, "elements", "申报要素", hsCode.elements, appliedLabels);
  next = fillProductTextFieldFromHsCode(next, "supervisionConditions", "监管条件", hsCode.supervisionConditions, appliedLabels);
  next = fillProductTextFieldFromHsCode(next, "inspectionCategory", "检验检疫类别", hsCode.inspectionCategory, appliedLabels);
  next = fillProductTextFieldFromHsCode(next, "unitCN", "中文单位", hsCode.unit, appliedLabels);

  const rebateRate = parseHsCodeRebateRate(hsCode.rebateRate);
  if (rebateRate != null && ExactDecimal.from(decimalText(readString(next, "taxRebateRate"))).compare(0) === 0) {
    next = { ...next, taxRebateRate: rebateRate };
    appliedLabels.push("退税率");
  }

  return {
    appliedLabels,
    changed: appliedLabels.length > 0,
    record: next,
  };
}

function fillProductTextFieldFromHsCode(
  record: MasterDataRecord,
  fieldName: string,
  label: string,
  value: string | undefined,
  appliedLabels: string[],
) {
  const normalized = normalizeText(value);
  if (!normalized || normalizeText(readString(record, fieldName))) {
    return record;
  }

  appliedLabels.push(label);
  return { ...record, [fieldName]: normalized };
}

function parseHsCodeRebateRate(value?: string) {
  const normalized = normalizeText(value).replace("%", "");
  const match = /-?\d+(?:\.\d+)?/.exec(normalized);
  if (!match) {
    return null;
  }

  try {
    const parsed = ExactDecimal.from(match[0]);
    return parsed.compare(0) >= 0 && parsed.compare(100) <= 0 ? parsed.normalize().toString() : null;
  } catch { return null; }
}

export function buildProductUnitAssistance(products: ApiProductDto[], units: ApiUnitDto[]): ProductUnitAssistance {
  const englishValues = [
    ...(units ?? []).map((unit) => unit.nameEN),
    ...(products ?? []).flatMap((product) => [product.unitEN, product.packageUnitEN]),
  ];
  const chineseValues = [
    ...(units ?? []).map((unit) => unit.nameCN),
    ...(products ?? []).flatMap((product) => [product.unitCN, product.packageUnitCN]),
  ];
  const suggestionsByEnglish = new Map<string, string[]>();

  for (const unit of units ?? []) {
    addProductUnitSuggestion(suggestionsByEnglish, unit.nameEN, unit.nameCN);
  }

  for (const product of products ?? []) {
    addProductUnitSuggestion(suggestionsByEnglish, product.unitEN, product.unitCN);
    addProductUnitSuggestion(suggestionsByEnglish, product.packageUnitEN, product.packageUnitCN);
  }

  return {
    chineseOptions: buildUniqueTextSuggestions(chineseValues),
    englishOptions: buildUniqueTextSuggestions(englishValues, true),
    suggestionsByEnglish,
  };
}

function addProductUnitSuggestion(map: Map<string, string[]>, englishUnit?: string, chineseUnit?: string) {
  const englishKey = normalizeUnitEnglishKey(englishUnit);
  const normalizedChinese = normalizeText(chineseUnit);
  if (!englishKey || !normalizedChinese) {
    return;
  }

  const values = map.get(englishKey) ?? [];
  if (!values.some((value) => value.toLowerCase() === normalizedChinese.toLowerCase())) {
    values.push(normalizedChinese);
    values.sort((left, right) => left.localeCompare(right, "zh-CN"));
  }

  map.set(englishKey, values);
}

function buildUniqueTextSuggestions(values: Array<string | undefined>, upperCase = false) {
  const suggestions = new Set<string>();
  for (const value of values) {
    const normalized = upperCase ? normalizeUnitEnglishKey(value) : normalizeText(value);
    if (normalized) {
      suggestions.add(normalized);
    }
  }

  return Array.from(suggestions).sort((left, right) => left.localeCompare(right, "zh-CN"));
}

export function normalizeUnitEnglishKey(value?: string) {
  return normalizeText(value).toUpperCase();
}

export function normalizeHsCodeKey(value?: string) {
  return normalizeText(value).replace(/[^0-9A-Za-z]/g, "").toUpperCase();
}

function findProductChineseUnitCandidates(assistance: ProductUnitAssistance, englishUnit: string) {
  return assistance.suggestionsByEnglish.get(normalizeUnitEnglishKey(englishUnit)) ?? [];
}

export function autoPopulateProductUnitFields(
  record: MasterDataRecord,
  assistance: ProductUnitAssistance,
  autoFilledTargets: Partial<Record<ProductUnitTargetField, string>>,
) {
  let nextRecord = record;
  for (const sourceField of Object.keys(productUnitLookupTargets) as ProductUnitSourceField[]) {
    const result = autoPopulateSingleProductUnitField(nextRecord, sourceField, assistance, autoFilledTargets);
    if (result.changed) {
      nextRecord = result.record;
    }
  }

  return { record: nextRecord };
}

export function autoPopulateSingleProductUnitField(
  record: MasterDataRecord,
  sourceField: ProductUnitSourceField,
  assistance: ProductUnitAssistance,
  autoFilledTargets: Partial<Record<ProductUnitTargetField, string>>,
):
  | {
      autoFilledValue: string;
      candidateCount: number;
      changed: true;
      record: MasterDataRecord;
      targetField: ProductUnitTargetField;
      targetLabel: string;
    }
  | {
      candidateCount: number;
      changed: false;
      record: MasterDataRecord;
    } {
  const target = productUnitLookupTargets[sourceField];
  const candidates = findProductChineseUnitCandidates(assistance, readString(record, sourceField));
  if (candidates.length !== 1) {
    return { candidateCount: candidates.length, changed: false, record };
  }

  const candidate = candidates[0];
  const currentTarget = normalizeText(readString(record, target.targetField));
  const currentAutoFilledValue = autoFilledTargets[target.targetField];
  const canReplace = !currentTarget || currentTarget === currentAutoFilledValue;
  if (!canReplace || currentTarget === candidate) {
    return { candidateCount: candidates.length, changed: false, record };
  }

  return {
    autoFilledValue: candidate,
    candidateCount: candidates.length,
    changed: true,
    record: { ...record, [target.targetField]: candidate },
    targetField: target.targetField,
    targetLabel: target.targetLabel,
  };
}

export function readRecordNumber(record: MasterDataRecord, name: string) {
  const value = record[name];
  if (typeof value === "number") {
    return value;
  }

  if (typeof value === "string") {
    const next = Number(value);
    return Number.isFinite(next) ? next : 0;
  }

  return 0;
}

function readDisplayString(value: unknown) {
  if (typeof value === "number") {
    return String(value);
  }

  if (typeof value === "string" && value.trim()) {
    return value;
  }

  return "-";
}

export function normalizeTextFields<const K extends string>(record: MasterDataRecord, id: number, fields: readonly K[]) {
  // The assertion is confined to this key-preserving Object.fromEntries projection.
  const text = Object.fromEntries(fields.map(field => [field, normalizeText(readString(record, field))])) as Record<K, string>;
  return { ...record, id, ...text } as MasterDataRecord & Record<K, string>;
}

export function isSeparateNotifyParty(record: MasterDataRecord) {
  return readString(record, "notifyPartyMode") === "Separate";
}

export function applyCustomerFieldChange(
  record: MasterDataRecord,
  name: string,
  value: string | number,
) {
  const next = { ...record, [name]: value };
  if (name === "notifyPartyMode" && value !== "Separate") {
    next.notifyPartyName = "";
    next.notifyPartyAddress = "";
  }

  return next;
}

export function normalizeCustomerRecord(record: MasterDataRecord, id: number) {
  const next = normalizeTextFields(record, id, customerTextFields);
  const requestedMode = readString(record, "notifyPartyMode");
  const notifyPartyMode: "None" | "SameAsConsignee" | "Separate" = requestedMode === "SameAsConsignee" || requestedMode === "Separate" ? requestedMode : "None";
  return { ...next, notifyPartyMode,
    notifyPartyName: notifyPartyMode === "Separate" ? next.notifyPartyName : "",
    notifyPartyAddress: notifyPartyMode === "Separate" ? next.notifyPartyAddress : "" };
}

export function encodeRouteId(value: string | number) {
  return encodeURIComponent(String(value));
}

export function numericRouteId(record: MasterDataRecord) {
  return encodeRouteId(numberValue(record.id));
}

export function parseNumericRouteKey(recordKey: string) {
  const id = Number(recordKey);
  return Number.isInteger(id) && id > 0 ? id : 0;
}

export function normalizeProductRecord(record: MasterDataRecord, id: number) {
  const decimals = Object.fromEntries(productDecimalFields.map(field => [field, decimalText(readString(record, field))])) as Record<typeof productDecimalFields[number], string>;
  return { ...normalizeTextFields(record, id, productTextFields), ...decimals };
}

export function normalizeHsCodeRecord(record: MasterDataRecord, id: number) {
  const next = normalizeTextFields(record, id, hsCodeTextFields);
  const updateTime = readString(record, "updateTime");
  if (updateTime) {
    next.updateTime = updateTime;
  }

  return next;
}

const customerTextFields = [
  "customerNameEN",
  "displayName",
  "notifyPartyName",
  "addressEN",
  "notifyPartyAddress",
  "contactPerson",
  "phone",
  "email",
  "taxId",
  "notes",
  "rowVersion",
] as const;

export const exporterTextFields = [
  "exporterNameEN",
  "exporterNameCN",
  "addressEN",
  "addressCN",
  "contactPerson",
  "creditCode",
  "customsCode",
  "phone",
  "bankName",
  "bankAccount",
  "swiftCode",
  "notes",
  "docSealPath",
  "customsSealPath",
  "rowVersion",
] as const;

export const payeeTextFields = [
  "category",
  "name",
  "bankName",
  "rmbAccount",
  "usdAccount",
  "contactPerson",
  "phone",
  "notes",
] as const;

const productTextFields = [
  "productCode",
  "nameEN",
  "nameCN",
  "description",
  "hsCode",
  "elements",
  "supervisionConditions",
  "inspectionCategory",
  "material",
  "brand",
  "origin",
  "unitEN",
  "unitCN",
  "packageUnitEN",
  "packageUnitCN",
] as const;

const productDecimalFields = [
  "taxRebateRate",
  "length",
  "width",
  "height",
  "gwPerCtn",
  "nwPerCtn",
  "pcsPerCtn",
  "defaultPrice",
] as const;

export const portTextFields = ["nameEN", "nameCN", "country", "code"] as const;
export const unitTextFields = ["nameEN", "nameCN", "code"] as const;
const hsCodeTextFields = [
  "code",
  "normalizedCode",
  "name",
  "unit",
  "description",
  "elements",
  "supervisionConditions",
  "inspectionCategory",
  "rebateRate",
  "detailUrl",
  "status",
  "sourceName",
  "replacedByCodes",
  "normalTariffRate",
  "preferentialTariffRate",
  "exportTariffRate",
  "consumptionTaxRate",
  "valueAddedTaxRate",
  "notes",
] as const;
