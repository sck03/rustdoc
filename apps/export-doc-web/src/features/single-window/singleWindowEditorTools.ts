import type {
  ApiAgentConsignmentDocumentDto,
  ApiCustomsCooAttachmentDto,
  ApiCustomsCooDocumentDto,
  ApiCustomsCooItemDto,
} from "../../api/index.ts";

type MutableRecord = Record<string, unknown>;

export type EditorToolResult<TDocument> = {
  changedCount: number;
  document: TDocument;
};

export type SingleWindowScopedClearOption = {
  key: string;
  label: string;
  description: string;
};

export const cooScopedClearOptionsByGroup: Record<string, readonly SingleWindowScopedClearOption[]> = {
  证书基础: [
    { key: "certificate_type", label: "证书类型与状态", description: "只恢复申请类型、证书类别和证书类型。" },
    { key: "enterprise_code", label: "企业主体信息", description: "只恢复企业名称、企业编号、出口商代码和录入企业代码。" },
  ],
  申报与对象: [
    { key: "declarant", label: "申报联系人", description: "只恢复申报员姓名、证件号、电话和申请日期。" },
    { key: "organization_address", label: "机构与地址", description: "只恢复签证机构、领证机构、申请地址和企业联系人。" },
    { key: "destination_parties", label: "发票对象与目的国", description: "只恢复发票日期、发票号、进口国、出口商、收货人及联系方式。" },
  ],
  运输与贸易: [
    { key: "goods_notes", label: "品名与备注", description: "只恢复商品/特别条款、唛头和申请书备注。" },
    { key: "transport_route", label: "运输港口", description: "只恢复运输方式、船名航次、中转国和港口信息。" },
    { key: "trade_terms", label: "贸易条款", description: "只恢复贸易方式、金额、合同号、信用证号、发票特殊条款、价格条款和币制。" },
  ],
  补充与特殊项: [
    { key: "certificate_extra", label: "证书补充", description: "只恢复证书备注、日期扩展、报关单号和企业承诺代码。" },
    { key: "producer_third_party", label: "生产商与第三方发票", description: "只恢复生产商信息及第三方发票标志。" },
    { key: "origin_country", label: "原产国信息", description: "只恢复原产国代码和名称。" },
  ],
  更改与重发: [
    { key: "modification_reissue", label: "更改与重发", description: "只恢复原证书号、更改/重发原因、更改栏目和原证日期。" },
  ],
  商品明细: [
    { key: "goods_name_pack", label: "品名与包装", description: "只恢复明细里的品名、包装和描述。" },
    { key: "goods_quantity_weight", label: "数量重量", description: "只恢复数量、单位、价格和毛净重。" },
    { key: "goods_origin_standard", label: "编码与原产标准", description: "只恢复 HS 编码、原产标准和原产国。" },
    { key: "goods_producer", label: "生产商信息", description: "只恢复生产商名称和联系方式。" },
  ],
  附件: [
    { key: "attachment_identity", label: "资料标识", description: "只清附件的证书类型、企业代码和文档类型说明。" },
    { key: "attachment_note_delay", label: "说明与提交", description: "只清附件说明和延迟提交标志。" },
  ],
};

export const agentScopedClearOptionsByGroup: Record<string, readonly SingleWindowScopedClearOption[]> = {
  基础标识: [
    { key: "identity", label: "企业与操作", description: "只恢复企业内部编号和操作类型。" },
    { key: "goods", label: "签名与货物", description: "只恢复数字签名、主要货物名称和 HS 编码。" },
  ],
  申报要素: [
    { key: "schedule_doc", label: "日期与单号", description: "只恢复进出口日期和提单号。" },
    { key: "trade_code", label: "贸易与编码", description: "只恢复贸易方式、原产地、经营/申报单位和币制。" },
    { key: "quantity_note", label: "数量与补充", description: "只恢复总价、数量重量、包装情况和其他要求。" },
  ],
  单证与费用: [
    { key: "contact", label: "联系电话", description: "只恢复委托方电话和被委托方电话。" },
    { key: "receipt", label: "收件信息", description: "只恢复收到证件日期、收到单证情况和其他收件信息。" },
    { key: "document_fee", label: "单证与费用", description: "只恢复报关单编号、报关收费和承诺说明。" },
  ],
};

const agentEditableFields: Array<keyof ApiAgentConsignmentDocumentDto> = [
  "copCusCode",
  "sign",
  "operType",
  "gName",
  "codeTS",
  "declTotal",
  "ieDate",
  "listNo",
  "tradeMode",
  "oriCountry",
  "tradeCode",
  "agentCode",
  "curr",
  "qtyOrWeight",
  "packingCondition",
  "otherNote",
  "consignTele",
  "entryId",
  "receiveDate",
  "paperInfo",
  "otherRecInfo",
  "declarePrice",
  "promiseNote",
  "declTele",
];

const agentManualOverrideFields: Array<keyof ApiAgentConsignmentDocumentDto> = [
  "sign",
  "packingCondition",
  "otherNote",
  "entryId",
  "paperInfo",
  "otherRecInfo",
  "declarePrice",
  "promiseNote",
  "declTele",
];

const cooDefaultableHeaderFields: Array<keyof ApiCustomsCooDocumentDto> = [
  "etpsName",
  "applyType",
  "certStatus",
  "certNo",
  "certType",
  "entMgrNo",
  "ciqRegNo",
  "aplRegNo",
  "applName",
  "applicant",
  "applTel",
  "orgCode",
  "fetchPlace",
  "aplAdd",
  "invDate",
  "invNo",
  "aplDate",
  "destCountry",
  "destCountryCode",
  "destCountryName",
  "exporter",
  "consignee",
  "goodsSpecClause",
  "mark",
  "loadPort",
  "unloadPort",
  "transMeans",
  "transName",
  "transCountryCode",
  "transCountryName",
  "transPort",
  "destPort",
  "transDetails",
  "intendExpDate",
  "tradeModeCode",
  "fobValue",
  "totalAmt",
  "contractNo",
  "note",
  "lcNo",
  "specInvTerms",
  "priceTerms",
  "curr",
  "remark",
  "producer",
  "producerSertFlag",
  "exhibitFlag",
  "thirdPartyInvFlag",
  "exporterTel",
  "exporterFax",
  "exporterEmail",
  "consigneeTel",
  "consigneeFax",
  "consigneeEmail",
  "predictFlag",
  "expDeclDate",
  "oriCountryCode",
  "oriCountry",
  "chkValidDate",
  "etpsConcEr",
  "etpsTel",
  "entryId",
  "prcsAssembly",
  "oldCertNo",
  "modReason",
  "modColm",
  "oldSituDesc",
  "modSituDesc",
  "oldDeclDate",
  "oldIssueDate",
  "aplPromiseCode",
];

const cooManualOverrideHeaderFields: Array<keyof ApiCustomsCooDocumentDto> = [
  "entMgrNo",
  "aplRegNo",
  "applName",
  "applicant",
  "applTel",
  "orgCode",
  "fetchPlace",
  "aplAdd",
  "transName",
  "transCountryCode",
  "transCountryName",
  "transPort",
  "transDetails",
  "note",
  "specInvTerms",
  "remark",
  "producer",
  "producerSertFlag",
  "exhibitFlag",
  "thirdPartyInvFlag",
  "exporterTel",
  "exporterFax",
  "exporterEmail",
  "consigneeTel",
  "consigneeFax",
  "consigneeEmail",
  "predictFlag",
  "expDeclDate",
  "chkValidDate",
  "etpsConcEr",
  "etpsTel",
  "entryId",
  "prcsAssembly",
  "oldCertNo",
  "modReason",
  "modColm",
  "oldSituDesc",
  "modSituDesc",
  "oldDeclDate",
  "oldIssueDate",
];

const cooDefaultableGoodsFields: Array<keyof ApiCustomsCooItemDto> = [
  "goodsItemFlag",
  "goodsName",
  "goodsNameE",
  "packQty",
  "packUnit",
  "packType",
  "goodsDesc",
  "goodsQty",
  "goodsQtyRef",
  "goodsUnitE",
  "goodsUnit",
  "goodsUnitRef",
  "secdGoodsQtyRef",
  "secdGoodsUnitRef",
  "grossWt",
  "netWt",
  "wtUnit",
  "invPrice",
  "invValue",
  "fobValue",
  "hsCode",
  "oriCriteria",
  "oriCriteriaRef",
  "oriCriteriaSub",
  "goodsOriginCountry",
  "goodsOriginCountryEn",
  "invNo",
  "iCompPrpr",
  "producer",
  "producerTel",
  "producerFax",
  "producerEmail",
  "ciqRegNo",
  "prdcEtpsName",
  "prdcEtpsConcEr",
  "prdcEtpsTel",
  "producerSertFlag",
  "goodsTaxRate",
];

const cooManualOverrideGoodsFields: Array<keyof ApiCustomsCooItemDto> = [
  "oriCriteria",
  "oriCriteriaRef",
  "iCompPrpr",
  "producer",
  "producerTel",
  "producerFax",
  "producerEmail",
  "ciqRegNo",
  "prdcEtpsName",
  "prdcEtpsConcEr",
  "prdcEtpsTel",
  "producerSertFlag",
  "oriCriteriaSub",
  "goodsTaxRate",
];

const agentScopedFieldKeysByCategory: Record<string, readonly (keyof ApiAgentConsignmentDocumentDto)[]> = {
  identity: ["copCusCode", "operType"],
  goods: ["sign", "gName", "codeTS"],
  schedule_doc: ["ieDate", "listNo"],
  trade_code: ["tradeMode", "oriCountry", "tradeCode", "agentCode", "curr"],
  quantity_note: ["declTotal", "qtyOrWeight", "packingCondition", "otherNote"],
  contact: ["consignTele", "declTele"],
  receipt: ["receiveDate", "paperInfo", "otherRecInfo"],
  document_fee: ["entryId", "declarePrice", "promiseNote"],
};

const cooScopedHeaderFieldKeysByCategory: Record<string, readonly (keyof ApiCustomsCooDocumentDto)[]> = {
  certificate_type: ["applyType", "certStatus", "certNo", "certType"],
  enterprise_code: ["etpsName", "entMgrNo", "ciqRegNo", "aplRegNo"],
  declarant: ["applName", "applicant", "applTel", "aplDate"],
  organization_address: ["orgCode", "fetchPlace", "aplAdd", "etpsConcEr", "etpsTel"],
  destination_parties: [
    "invDate",
    "invNo",
    "destCountry",
    "destCountryCode",
    "destCountryName",
    "exporter",
    "consignee",
    "exporterTel",
    "exporterFax",
    "exporterEmail",
    "consigneeTel",
    "consigneeFax",
    "consigneeEmail",
  ],
  goods_notes: ["goodsSpecClause", "mark", "note"],
  transport_route: [
    "loadPort",
    "unloadPort",
    "transMeans",
    "transName",
    "transCountryCode",
    "transCountryName",
    "transPort",
    "destPort",
    "transDetails",
    "intendExpDate",
    "predictFlag",
    "expDeclDate",
  ],
  trade_terms: ["tradeModeCode", "fobValue", "totalAmt", "contractNo", "lcNo", "specInvTerms", "priceTerms", "curr"],
  certificate_extra: [
    "remark",
    "chkValidDate",
    "entryId",
    "prcsAssembly",
    "aplPromiseCode",
  ],
  modification_reissue: [
    "oldCertNo",
    "modReason",
    "modColm",
    "oldSituDesc",
    "modSituDesc",
    "oldDeclDate",
    "oldIssueDate",
  ],
  producer_third_party: ["producer", "producerSertFlag", "exhibitFlag", "thirdPartyInvFlag"],
  origin_country: ["oriCountryCode", "oriCountry"],
};

const cooScopedGoodsFieldKeysByCategory: Record<string, readonly (keyof ApiCustomsCooItemDto)[]> = {
  goods_name_pack: ["goodsItemFlag", "goodsName", "goodsNameE", "packQty", "packUnit", "packType", "goodsDesc"],
  goods_quantity_weight: [
    "goodsQty",
    "goodsQtyRef",
    "goodsUnitE",
    "goodsUnit",
    "goodsUnitRef",
    "secdGoodsQtyRef",
    "secdGoodsUnitRef",
    "grossWt",
    "netWt",
    "wtUnit",
    "invPrice",
    "invValue",
    "fobValue",
  ],
  goods_origin_standard: ["hsCode", "oriCriteria", "oriCriteriaRef", "oriCriteriaSub", "goodsOriginCountry", "goodsOriginCountryEn", "invNo"],
  goods_producer: [
    "iCompPrpr",
    "producer",
    "producerTel",
    "producerFax",
    "producerEmail",
    "ciqRegNo",
    "prdcEtpsName",
    "prdcEtpsConcEr",
    "prdcEtpsTel",
    "producerSertFlag",
    "goodsTaxRate",
  ],
};

const cooScopedAttachmentStringFieldKeysByCategory: Record<string, readonly (keyof ApiCustomsCooAttachmentDto)[]> = {
  attachment_identity: ["certType", "aplRegNo", "ciqRegNo", "fileType", "docType"],
  attachment_note_delay: ["description"],
};

const cooHeaderDefaultFallbacks: Partial<Record<keyof ApiCustomsCooDocumentDto, string>> = {
  applyType: "0",
  certStatus: "0",
  certType: "C",
  aplPromiseCode: "1",
};

const agentDefaultFallbacks: Partial<Record<keyof ApiAgentConsignmentDocumentDto, string>> = {
  operType: "1",
};

export function cloneEditorDocument<TDocument>(document: TDocument): TDocument {
  if (typeof structuredClone === "function") {
    return structuredClone(document);
  }

  return JSON.parse(JSON.stringify(document)) as TDocument;
}

export function areEditorDocumentsEqual<TDocument>(left: TDocument, right: TDocument) {
  return JSON.stringify(left) === JSON.stringify(right);
}

export function applyAgentDefaultsToEmptyFields(
  current: ApiAgentConsignmentDocumentDto,
  defaults: ApiAgentConsignmentDocumentDto,
): EditorToolResult<ApiAgentConsignmentDocumentDto> {
  const document = cloneEditorDocument(current);
  const changedCount = copyStringFields(document, defaults, agentEditableFields, { onlyEmpty: true, skipEmptyDefault: true });
  return { changedCount, document };
}

export function clearAgentManualOverrides(
  current: ApiAgentConsignmentDocumentDto,
): EditorToolResult<ApiAgentConsignmentDocumentDto> {
  const document = cloneEditorDocument(current);
  const changedCount = clearStringFields(document, agentManualOverrideFields);
  return { changedCount, document };
}

export function applyAgentDefaultsForScope(
  current: ApiAgentConsignmentDocumentDto,
  defaults: ApiAgentConsignmentDocumentDto,
  groupKey: string,
  categoryKey = "",
): EditorToolResult<ApiAgentConsignmentDocumentDto> {
  const document = cloneEditorDocument(current);
  const categoryKeys = resolveScopeCategoryKeys(agentScopedClearOptionsByGroup, groupKey, categoryKey);
  const fieldKeys = resolveScopedFieldKeys(categoryKeys, agentScopedFieldKeysByCategory);
  const changedCount = copyStringFields(document, defaults, fieldKeys, {
    onlyEmpty: false,
    skipEmptyDefault: false,
    fallbackValues: agentDefaultFallbacks,
  });

  return { changedCount, document };
}

export function applyCooDefaultsToEmptyFields(
  current: ApiCustomsCooDocumentDto,
  defaults: ApiCustomsCooDocumentDto,
): EditorToolResult<ApiCustomsCooDocumentDto> {
  const document = cloneEditorDocument(current);
  let changedCount = copyStringFields(document, defaults, cooDefaultableHeaderFields, {
    onlyEmpty: true,
    skipEmptyDefault: true,
  });
  changedCount += mergeCooGoodsDefaults(document, defaults, { onlyEmpty: true, skipEmptyDefault: true });
  return { changedCount, document };
}

export function clearCooManualOverrides(
  current: ApiCustomsCooDocumentDto,
): EditorToolResult<ApiCustomsCooDocumentDto> {
  const document = cloneEditorDocument(current);
  let changedCount = clearStringFields(document, cooManualOverrideHeaderFields);
  for (const item of document.items) {
    changedCount += clearStringFields(item, cooManualOverrideGoodsFields);
  }

  return { changedCount, document };
}

export function applyCooDefaultsForScope(
  current: ApiCustomsCooDocumentDto,
  defaults: ApiCustomsCooDocumentDto,
  groupKey: string,
  categoryKey = "",
): EditorToolResult<ApiCustomsCooDocumentDto> {
  const document = cloneEditorDocument(current);
  const categoryKeys = resolveScopeCategoryKeys(cooScopedClearOptionsByGroup, groupKey, categoryKey);
  let changedCount = 0;

  if (groupKey === "商品明细") {
    changedCount += mergeCooGoodsDefaultsForScope(document, defaults, categoryKeys);
  } else if (groupKey === "附件") {
    changedCount += clearCooAttachmentScope(document, categoryKeys);
  } else {
    changedCount += copyStringFields(document, defaults, resolveScopedFieldKeys(categoryKeys, cooScopedHeaderFieldKeysByCategory), {
      onlyEmpty: false,
      skipEmptyDefault: false,
      fallbackValues: cooHeaderDefaultFallbacks,
    });
  }

  return { changedCount, document };
}

function mergeCooGoodsDefaults(
  document: ApiCustomsCooDocumentDto,
  defaults: ApiCustomsCooDocumentDto,
  options: { onlyEmpty: boolean; skipEmptyDefault: boolean },
) {
  let changedCount = 0;
  const maxIndex = Math.min(document.items.length, defaults.items.length);
  for (let index = 0; index < maxIndex; index += 1) {
    changedCount += copyStringFields(document.items[index], defaults.items[index], cooDefaultableGoodsFields, options);
  }

  return changedCount;
}

function mergeCooGoodsDefaultsForScope(
  document: ApiCustomsCooDocumentDto,
  defaults: ApiCustomsCooDocumentDto,
  categoryKeys: readonly string[],
) {
  let changedCount = 0;
  const fieldKeys = resolveScopedFieldKeys(categoryKeys, cooScopedGoodsFieldKeysByCategory);
  const maxIndex = Math.min(document.items.length, defaults.items.length);
  for (let index = 0; index < maxIndex; index += 1) {
    changedCount += copyStringFields(document.items[index], defaults.items[index], fieldKeys, {
      onlyEmpty: false,
      skipEmptyDefault: false,
    });
  }

  return changedCount;
}

function clearCooAttachmentScope(document: ApiCustomsCooDocumentDto, categoryKeys: readonly string[]) {
  let changedCount = 0;
  const fieldKeys = resolveScopedFieldKeys(categoryKeys, cooScopedAttachmentStringFieldKeysByCategory);
  const shouldClearDelay = categoryKeys.includes("attachment_note_delay");
  for (const attachment of document.attachments) {
    changedCount += clearStringFields(attachment, fieldKeys);
    if (shouldClearDelay && attachment.isDelay) {
      attachment.isDelay = false;
      changedCount += 1;
    }
  }

  return changedCount;
}

function resolveScopeCategoryKeys(
  optionsByGroup: Record<string, readonly SingleWindowScopedClearOption[]>,
  groupKey: string,
  categoryKey: string,
) {
  if (categoryKey.trim()) {
    return [categoryKey.trim()];
  }

  return (optionsByGroup[groupKey] ?? []).map((option) => option.key);
}

function resolveScopedFieldKeys<TField extends string>(
  categoryKeys: readonly string[],
  fieldKeysByCategory: Record<string, readonly TField[]>,
) {
  return Array.from(new Set(categoryKeys.flatMap((key) => fieldKeysByCategory[key] ?? [])));
}

function copyStringFields<TRecord extends object>(
  target: TRecord,
  source: TRecord,
  fields: readonly (keyof TRecord)[],
  options: { onlyEmpty: boolean; skipEmptyDefault: boolean; fallbackValues?: Partial<Record<keyof TRecord, string>> },
) {
  let changedCount = 0;
  const targetRecord = target as MutableRecord;
  const sourceRecord = source as MutableRecord;
  for (const field of fields) {
    const key = String(field);
    const currentValue = readString(targetRecord, key);
    const defaultValue = readString(sourceRecord, key).trim();
    const nextValue = defaultValue || options.fallbackValues?.[field] || "";
    if (options.onlyEmpty && currentValue.trim()) {
      continue;
    }

    if (options.skipEmptyDefault && !nextValue) {
      continue;
    }

    if (currentValue !== nextValue) {
      targetRecord[key] = nextValue;
      changedCount += 1;
    }
  }

  return changedCount;
}

function clearStringFields<TRecord extends object>(target: TRecord, fields: readonly (keyof TRecord)[]) {
  let changedCount = 0;
  const targetRecord = target as MutableRecord;
  for (const field of fields) {
    const key = String(field);
    if (readString(targetRecord, key).trim()) {
      targetRecord[key] = "";
      changedCount += 1;
    }
  }

  return changedCount;
}

function readString(target: MutableRecord, key: string) {
  const value = target[key];
  return typeof value === "string" ? value : "";
}
