import type {
ApiCustomerDto,
ApiExporterDto,
ApiHsCodeDto,
ApiPayeeDto,
ApiPortDto,
ApiProductDto,
ApiUnitDto,
} from "../../api/index.ts";
import { formatAmount,formatDate,numberValue } from "../../ui/formUtils.ts";
import {
customerTextFields,
encodeRouteId,
exporterTextFields,
normalizeArrayPage,
normalizeHsCodeRecord,
normalizeProductRecord,
normalizeTextFields,
numericRouteId,
parseNumericRouteKey,
payeeTextFields,
portTextFields,
readString,
unitTextFields
} from "./masterDataModel.ts";
import type { MasterDataEntityConfig,MasterDataRecord } from "./masterDataTypes.ts";


export const masterDataConfigs: MasterDataEntityConfig[] = [
  {
    key: "customers",
    label: "客户",
    listLabel: "客户列表",
    newLabel: "新建客户",
    editLabel: "编辑客户",
    searchPlaceholder: "客户名称、通知人、联系人、电话、邮箱",
    primaryField: "displayName",
    routeId: numericRouteId,
    columns: [
      { name: "displayName", label: "显示名" },
      { name: "customerNameEN", label: "英文名" },
      { name: "notifyPartyName", label: "通知人" },
      { name: "contactPerson", label: "联系人" },
      { name: "phone", label: "电话" },
      { name: "email", label: "邮箱" },
    ],
    sections: [
      {
        title: "基础信息",
        fields: [
          { name: "customerNameEN", label: "客户英文名", required: true, className: "field-grid-span-2" },
          { name: "notifyPartyName", label: "通知人" },
          { name: "contactPerson", label: "联系人" },
          { name: "phone", label: "电话" },
          { name: "email", label: "邮箱" },
          { name: "taxId", label: "税号" },
        ],
      },
      {
        title: "地址和备注",
        fields: [
          { name: "addressEN", label: "客户地址", type: "textarea", className: "field-grid-span-2" },
          { name: "notifyPartyAddress", label: "通知人地址", type: "textarea", className: "field-grid-span-2" },
          { name: "notes", label: "备注", type: "textarea", className: "field-grid-span-2" },
        ],
      },
    ],
    emptyRecord: () => ({
      id: 0,
      customerNameEN: "",
      displayName: "",
      notifyPartyName: "",
      addressEN: "",
      notifyPartyAddress: "",
      contactPerson: "",
      phone: "",
      email: "",
      taxId: "",
      notes: "",
      rowVersion: "",
    }),
    normalizeRecord: (record, id) => normalizeTextFields(record, id, customerTextFields),
    list: async (client, request) =>
      normalizeArrayPage(
        (await client.listCustomers({ keyword: request.keyword || undefined })) as unknown as MasterDataRecord[],
        request.pageNumber,
        request.pageSize,
      ),
    get: async (client, recordKey) =>
      (await client.getCustomer({ id: parseNumericRouteKey(recordKey) })) as unknown as MasterDataRecord,
    create: async (client, record) => (await client.createCustomer({ body: record as unknown as ApiCustomerDto })) as unknown as MasterDataRecord,
    update: async (client, recordKey, record) =>
      (await client.updateCustomer({ id: parseNumericRouteKey(recordKey), body: record as unknown as ApiCustomerDto })) as unknown as MasterDataRecord,
    delete: (client, record) => client.deleteCustomer({ id: numberValue(record.id) }),
  },
  {
    key: "exporters",
    label: "出口商",
    listLabel: "出口商列表",
    newLabel: "新建出口商",
    editLabel: "编辑出口商",
    searchPlaceholder: "出口商名称、海关编码、信用代码、银行",
    primaryField: "exporterNameEN",
    routeId: numericRouteId,
    columns: [
      { name: "exporterNameEN", label: "英文名" },
      { name: "exporterNameCN", label: "中文名" },
      { name: "creditCode", label: "信用代码" },
      { name: "customsCode", label: "海关编码" },
      { name: "bankName", label: "银行" },
      { name: "bankAccount", label: "账号" },
    ],
    sections: [
      {
        title: "基础信息",
        fields: [
          { name: "exporterNameEN", label: "出口商英文名", required: true, className: "field-grid-span-2" },
          { name: "exporterNameCN", label: "出口商中文名", className: "field-grid-span-2" },
          { name: "contactPerson", label: "联系人" },
          { name: "phone", label: "电话" },
          { name: "creditCode", label: "统一社会信用代码" },
          { name: "customsCode", label: "海关编码" },
        ],
      },
      {
        title: "地址和银行",
        fields: [
          { name: "addressEN", label: "英文地址", type: "textarea", className: "field-grid-span-2" },
          { name: "addressCN", label: "中文地址", type: "textarea", className: "field-grid-span-2" },
          { name: "bankName", label: "银行", className: "field-grid-span-2" },
          { name: "bankAccount", label: "账号", className: "field-grid-span-2" },
          { name: "swiftCode", label: "Swift Code" },
        ],
      },
      {
        title: "印章和备注",
        fields: [
          { name: "docSealPath", label: "单证章路径", pathPicker: "exporterSealImage" },
          { name: "customsSealPath", label: "报关章路径", pathPicker: "exporterSealImage" },
          { name: "notes", label: "备注", type: "textarea", className: "field-grid-span-2" },
        ],
      },
    ],
    emptyRecord: () => ({
      id: 0,
      exporterNameEN: "",
      exporterNameCN: "",
      addressEN: "",
      addressCN: "",
      contactPerson: "",
      creditCode: "",
      customsCode: "",
      phone: "",
      bankName: "",
      bankAccount: "",
      swiftCode: "",
      notes: "",
      docSealPath: "",
      customsSealPath: "",
      rowVersion: "",
    }),
    normalizeRecord: (record, id) => normalizeTextFields(record, id, exporterTextFields),
    list: async (client, request) =>
      normalizeArrayPage(
        (await client.listExporters({ keyword: request.keyword || undefined })) as unknown as MasterDataRecord[],
        request.pageNumber,
        request.pageSize,
      ),
    get: async (client, recordKey) =>
      (await client.getExporter({ id: parseNumericRouteKey(recordKey) })) as unknown as MasterDataRecord,
    create: async (client, record) => (await client.createExporter({ body: record as unknown as ApiExporterDto })) as unknown as MasterDataRecord,
    update: async (client, recordKey, record) =>
      (await client.updateExporter({ id: parseNumericRouteKey(recordKey), body: record as unknown as ApiExporterDto })) as unknown as MasterDataRecord,
    delete: (client, record) => client.deleteExporter({ id: numberValue(record.id) }),
  },
  {
    key: "payees",
    label: "收款对象",
    listLabel: "收款对象列表",
    newLabel: "新建收款对象",
    editLabel: "编辑收款对象",
    searchPlaceholder: "名称、分类、银行、联系人、电话",
    primaryField: "name",
    routeId: numericRouteId,
    columns: [
      { name: "name", label: "名称" },
      { name: "category", label: "分类" },
      { name: "bankName", label: "银行" },
      { name: "rmbAccount", label: "人民币账号" },
      { name: "usdAccount", label: "美元账号" },
      { name: "phone", label: "电话" },
    ],
    sections: [
      {
        title: "基础信息",
        fields: [
          { name: "name", label: "名称", required: true, className: "field-grid-span-2" },
          { name: "category", label: "分类", required: true, customOptionType: "PayeeCategory" },
          { name: "contactPerson", label: "联系人" },
          { name: "phone", label: "电话" },
        ],
      },
      {
        title: "银行和备注",
        fields: [
          { name: "bankName", label: "银行", className: "field-grid-span-2" },
          { name: "rmbAccount", label: "人民币账号", className: "field-grid-span-2" },
          { name: "usdAccount", label: "美元账号", className: "field-grid-span-2" },
          { name: "notes", label: "备注", type: "textarea", className: "field-grid-span-2" },
        ],
      },
    ],
    emptyRecord: () => ({
      id: 0,
      category: "",
      name: "",
      bankName: "",
      rmbAccount: "",
      usdAccount: "",
      contactPerson: "",
      phone: "",
      notes: "",
    }),
    normalizeRecord: (record, id) => normalizeTextFields(record, id, payeeTextFields),
    list: async (client, request) =>
      normalizeArrayPage(
        (await client.listPayees({ keyword: request.keyword || undefined })) as unknown as MasterDataRecord[],
        request.pageNumber,
        request.pageSize,
      ),
    get: async (client, recordKey) =>
      (await client.getPayee({ id: parseNumericRouteKey(recordKey) })) as unknown as MasterDataRecord,
    create: async (client, record) => (await client.createPayee({ body: record as unknown as ApiPayeeDto })) as unknown as MasterDataRecord,
    update: async (client, recordKey, record) =>
      (await client.updatePayee({ id: parseNumericRouteKey(recordKey), body: record as unknown as ApiPayeeDto })) as unknown as MasterDataRecord,
    delete: (client, record) => client.deletePayee({ id: numberValue(record.id) }),
  },
  {
    key: "products",
    label: "商品",
    listLabel: "商品列表",
    newLabel: "新建商品",
    editLabel: "编辑商品",
    searchPlaceholder: "商品编码、品名、HS 编码、品牌、原产地",
    primaryField: "nameCN",
    routeId: numericRouteId,
    columns: [
      { name: "productCode", label: "编码" },
      { name: "nameCN", label: "中文品名" },
      { name: "nameEN", label: "英文品名" },
      { name: "hsCode", label: "HS 编码" },
      { name: "origin", label: "原产地" },
      { name: "defaultPrice", label: "默认价", className: "amount-cell", format: (value) => formatAmount(numberValue(value as number), "") },
      { name: "updatedAt", label: "更新" , format: (value) => typeof value === "string" ? formatDate(value) : "-" },
    ],
    sections: [
      {
        title: "基础信息",
        fields: [
          { name: "productCode", label: "商品编码", productAssistanceField: "productCode" },
          { name: "nameCN", label: "中文品名", productAssistanceField: "nameCN", className: "field-grid-span-2" },
          { name: "nameEN", label: "英文品名", required: true, productAssistanceField: "nameEN", className: "field-grid-span-2" },
          { name: "hsCode", label: "HS 编码", productAssistanceField: "hsCode" },
          { name: "brand", label: "品牌", productAssistanceField: "brand" },
          { name: "origin", label: "原产地", productAssistanceField: "origin" },
          { name: "material", label: "材质", productAssistanceField: "material" },
        ],
      },
      {
        title: "申报信息",
        fields: [
          { name: "elements", label: "申报要素", type: "textarea", className: "field-grid-span-2" },
          { name: "description", label: "描述", type: "textarea", className: "field-grid-span-2" },
          { name: "supervisionConditions", label: "监管条件" },
          { name: "inspectionCategory", label: "检验检疫类别" },
          { name: "taxRebateRate", label: "退税率", type: "number" },
        ],
      },
      {
        title: "单位和装箱",
        fields: [
          { name: "unitEN", label: "英文单位" },
          { name: "unitCN", label: "中文单位" },
          { name: "packageUnitEN", label: "包装英文单位" },
          { name: "packageUnitCN", label: "包装中文单位" },
          { name: "length", label: "长", type: "number" },
          { name: "width", label: "宽", type: "number" },
          { name: "height", label: "高", type: "number" },
          { name: "gwPerCtn", label: "每箱毛重", type: "number" },
          { name: "nwPerCtn", label: "每箱净重", type: "number" },
          { name: "pcsPerCtn", label: "每箱数量", type: "number" },
          { name: "defaultPrice", label: "默认单价", type: "number" },
        ],
      },
    ],
    emptyRecord: () => ({
      id: 0,
      productCode: "",
      nameEN: "",
      nameCN: "",
      description: "",
      hsCode: "",
      elements: "",
      supervisionConditions: "",
      inspectionCategory: "",
      taxRebateRate: 0,
      material: "",
      brand: "",
      origin: "",
      unitEN: "",
      unitCN: "",
      length: 0,
      width: 0,
      height: 0,
      gwPerCtn: 0,
      nwPerCtn: 0,
      pcsPerCtn: 0,
      packageUnitEN: "",
      packageUnitCN: "",
      defaultPrice: 0,
    }),
    normalizeRecord: normalizeProductRecord,
    list: async (client, request) =>
      normalizeArrayPage(
        (await client.listProducts({ keyword: request.keyword || undefined })) as unknown as MasterDataRecord[],
        request.pageNumber,
        request.pageSize,
      ),
    get: async (client, recordKey) =>
      (await client.getProduct({ id: parseNumericRouteKey(recordKey) })) as unknown as MasterDataRecord,
    create: async (client, record) => (await client.createProduct({ body: record as unknown as ApiProductDto })) as unknown as MasterDataRecord,
    update: async (client, recordKey, record) =>
      (await client.updateProduct({ id: parseNumericRouteKey(recordKey), body: record as unknown as ApiProductDto })) as unknown as MasterDataRecord,
    delete: (client, record) => client.deleteProduct({ id: numberValue(record.id) }),
  },
  {
    key: "ports",
    label: "港口",
    listLabel: "港口列表",
    newLabel: "新建港口",
    editLabel: "编辑港口",
    searchPlaceholder: "中英文名称、国家、代码",
    primaryField: "nameEN",
    routeId: numericRouteId,
    columns: [
      { name: "nameEN", label: "英文名" },
      { name: "nameCN", label: "中文名" },
      { name: "country", label: "国家" },
      { name: "code", label: "代码" },
    ],
    sections: [
      {
        title: "基础信息",
        fields: [
          { name: "nameEN", label: "英文名", required: true },
          { name: "nameCN", label: "中文名" },
          { name: "country", label: "国家" },
          { name: "code", label: "代码" },
        ],
      },
    ],
    emptyRecord: () => ({ id: 0, nameEN: "", nameCN: "", country: "", code: "" }),
    normalizeRecord: (record, id) => normalizeTextFields(record, id, portTextFields),
    list: async (client, request) =>
      normalizeArrayPage(
        (await client.listPorts({ keyword: request.keyword || undefined })) as unknown as MasterDataRecord[],
        request.pageNumber,
        request.pageSize,
      ),
    get: async (client, recordKey) =>
      (await client.getPort({ id: parseNumericRouteKey(recordKey) })) as unknown as MasterDataRecord,
    create: async (client, record) => (await client.createPort({ body: record as unknown as ApiPortDto })) as unknown as MasterDataRecord,
    update: async (client, recordKey, record) =>
      (await client.updatePort({ id: parseNumericRouteKey(recordKey), body: record as unknown as ApiPortDto })) as unknown as MasterDataRecord,
    delete: (client, record) => client.deletePort({ id: numberValue(record.id) }),
  },
  {
    key: "units",
    label: "单位",
    listLabel: "单位列表",
    newLabel: "新建单位",
    editLabel: "编辑单位",
    searchPlaceholder: "中英文名称、代码",
    primaryField: "nameEN",
    routeId: numericRouteId,
    columns: [
      { name: "nameEN", label: "英文名" },
      { name: "nameCN", label: "中文名" },
      { name: "code", label: "代码" },
    ],
    sections: [
      {
        title: "基础信息",
        fields: [
          { name: "nameEN", label: "英文名", required: true },
          { name: "nameCN", label: "中文名" },
          { name: "code", label: "代码" },
        ],
      },
    ],
    emptyRecord: () => ({ id: 0, nameEN: "", nameCN: "", code: "" }),
    normalizeRecord: (record, id) => normalizeTextFields(record, id, unitTextFields),
    list: async (client, request) =>
      normalizeArrayPage(
        (await client.listUnits({ keyword: request.keyword || undefined })) as unknown as MasterDataRecord[],
        request.pageNumber,
        request.pageSize,
      ),
    get: async (client, recordKey) =>
      (await client.getUnit({ id: parseNumericRouteKey(recordKey) })) as unknown as MasterDataRecord,
    create: async (client, record) => (await client.createUnit({ body: record as unknown as ApiUnitDto })) as unknown as MasterDataRecord,
    update: async (client, recordKey, record) =>
      (await client.updateUnit({ id: parseNumericRouteKey(recordKey), body: record as unknown as ApiUnitDto })) as unknown as MasterDataRecord,
    delete: (client, record) => client.deleteUnit({ id: numberValue(record.id) }),
  },
  {
    key: "hs-codes",
    label: "HS 编码",
    listLabel: "HS 编码列表",
    newLabel: "新建 HS 编码",
    editLabel: "编辑 HS 编码",
    searchPlaceholder: "编码、品名、申报要素、监管条件",
    primaryField: "code",
    routeId: (record) => encodeRouteId(readString(record, "code") || numberValue(record.id)),
    columns: [
      { name: "code", label: "编码" },
      { name: "name", label: "名称" },
      { name: "unit", label: "单位" },
      { name: "rebateRate", label: "退税率" },
      { name: "supervisionConditions", label: "监管条件" },
      { name: "inspectionCategory", label: "检验检疫类别" },
      { name: "status", label: "状态", format: (value) => value === "SuspectedObsolete" ? "疑似作废" : value === "Obsolete" ? "已作废" : "有效" },
      { name: "updateTime", label: "更新", format: (value) => typeof value === "string" ? formatDate(value) : "-" },
    ],
    sections: [
      {
        title: "基础信息",
        fields: [
          { name: "code", label: "HS 编码", required: true, readOnlyOnEdit: true },
          { name: "name", label: "名称", required: true },
          { name: "unit", label: "单位" },
          { name: "rebateRate", label: "退税率" },
          { name: "supervisionConditions", label: "监管条件" },
          { name: "inspectionCategory", label: "检验检疫类别" },
          { name: "detailUrl", label: "详情链接" },
        ],
      },
      {
        title: "申报内容",
        fields: [
          { name: "elements", label: "申报要素", type: "textarea" },
          { name: "description", label: "描述", type: "textarea" },
        ],
      },
      {
        title: "数据来源与有效性",
        fields: [
          { name: "status", label: "状态（Active / SuspectedObsolete / Obsolete）" },
          { name: "sourceName", label: "数据来源" },
          { name: "effectiveYear", label: "适用年份", type: "number" },
          { name: "replacedByCodes", label: "替代编码（多个用逗号分隔）" },
        ],
      },
    ],
    emptyRecord: () => ({
      id: 0,
      code: "",
      normalizedCode: "",
      name: "",
      unit: "",
      description: "",
      elements: "",
      supervisionConditions: "",
      inspectionCategory: "",
      rebateRate: "",
      detailUrl: "",
      status: "Active",
      sourceName: "",
      effectiveYear: null,
      replacedByCodes: "",
    }),
    normalizeRecord: normalizeHsCodeRecord,
    list: async (client, request) => {
      const result = await client.listHsCodes({
        pageNumber: request.pageNumber,
        pageSize: request.pageSize,
        keyword: request.keyword || undefined,
      });

      return {
        hasNextPage: result.hasNextPage,
        hasPreviousPage: result.hasPreviousPage,
        items: result.items as unknown as MasterDataRecord[],
        pageNumber: result.pageNumber,
        pageSize: result.pageSize,
        totalCount: result.totalCount,
        totalPages: result.totalPages,
      };
    },
    get: async (client, recordKey) =>
      (await client.getHsCode({ code: decodeURIComponent(recordKey) })) as unknown as MasterDataRecord,
    create: async (client, record) => (await client.createHsCode({ body: record as unknown as ApiHsCodeDto })) as unknown as MasterDataRecord,
    update: async (client, recordKey, record) =>
      (await client.updateHsCode({ code: decodeURIComponent(recordKey), body: record as unknown as ApiHsCodeDto })) as unknown as MasterDataRecord,
    delete: (client, record) => client.deleteHsCode({ id: numberValue(record.id) }),
  },
];

export function getMasterDataConfig(key?: string): MasterDataEntityConfig | null {
  return masterDataConfigs.find((item) => item.key === key) ?? null;
}

export function getMasterDataConfigFromPath(pathname: string): MasterDataEntityConfig | null {
  const match = pathname.match(/^\/master-data\/([^/]+)/);
  return getMasterDataConfig(match?.[1]);
}
