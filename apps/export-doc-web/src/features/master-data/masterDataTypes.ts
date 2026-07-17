import type { ApiCommandResponse,ExportDocManagerApiClient } from "../../api/index.ts";

export const productHsCodeLookupPageSize = 200;
export const hsCodeClearAllConfirmationText = "CLEAR";

export type MasterDataEntityKey = "customers" | "exporters" | "payees" | "products" | "ports" | "units" | "hs-codes";
export type MasterDataRecord = Record<string, unknown> & { id: number };
export type MasterDataFieldType = "text" | "number" | "textarea";

export type MasterDataListResult = {
  hasNextPage: boolean;
  hasPreviousPage: boolean;
  items: MasterDataRecord[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type MasterDataColumnDefinition = {
  name: string;
  label: string;
  className?: string;
  format?: (value: unknown, record: MasterDataRecord) => string;
};

export type MasterDataFieldDefinition = {
  name: string;
  label: string;
  className?: string;
  type?: MasterDataFieldType;
  customOptionType?: string;
  pathPicker?: "exporterSealImage";
  productAssistanceField?: ProductAssistanceField;
  required?: boolean;
  readOnlyOnEdit?: boolean;
};

export type MasterDataSectionDefinition = {
  title: string;
  fields: MasterDataFieldDefinition[];
};

export type MasterDataEntityConfig = {
  key: MasterDataEntityKey;
  label: string;
  listLabel: string;
  newLabel: string;
  editLabel: string;
  searchPlaceholder: string;
  primaryField: string;
  routeId: (record: MasterDataRecord) => string;
  columns: MasterDataColumnDefinition[];
  sections: MasterDataSectionDefinition[];
  emptyRecord: () => MasterDataRecord;
  normalizeRecord: (record: MasterDataRecord, id: number) => MasterDataRecord;
  list: (client: ExportDocManagerApiClient, request: { keyword: string; pageNumber: number; pageSize: number }) => Promise<MasterDataListResult>;
  get: (client: ExportDocManagerApiClient, recordKey: string) => Promise<MasterDataRecord>;
  create: (client: ExportDocManagerApiClient, record: MasterDataRecord) => Promise<MasterDataRecord>;
  update: (client: ExportDocManagerApiClient, recordKey: string, record: MasterDataRecord) => Promise<MasterDataRecord>;
  delete: (client: ExportDocManagerApiClient, record: MasterDataRecord, recordKey: string) => Promise<ApiCommandResponse>;
};

export type ProductUnitSourceField = "unitEN" | "packageUnitEN";
export type ProductUnitTargetField = "unitCN" | "packageUnitCN";
export type ProductAssistanceField = "productCode" | "nameEN" | "nameCN" | "hsCode" | "material" | "brand" | "origin";

export type ProductUnitLookupTarget = {
  sourceField: ProductUnitSourceField;
  targetField: ProductUnitTargetField;
  targetLabel: string;
};

export type ProductUnitAssistance = {
  chineseOptions: string[];
  englishOptions: string[];
  suggestionsByEnglish: Map<string, string[]>;
};

export type ProductInputAssistance = Record<ProductAssistanceField, string[]>;

export const emptyProductUnitAssistance: ProductUnitAssistance = {
  chineseOptions: [],
  englishOptions: [],
  suggestionsByEnglish: new Map<string, string[]>(),
};

export const emptyProductInputAssistance: ProductInputAssistance = {
  brand: [],
  hsCode: [],
  material: [],
  nameCN: [],
  nameEN: [],
  origin: [],
  productCode: [],
};

export const productUnitLookupTargets: Record<ProductUnitSourceField, ProductUnitLookupTarget> = {
  unitEN: {
    sourceField: "unitEN",
    targetField: "unitCN",
    targetLabel: "中文单位",
  },
  packageUnitEN: {
    sourceField: "packageUnitEN",
    targetField: "packageUnitCN",
    targetLabel: "包装中文单位",
  },
};
