import {
  ApiSingleWindowReferenceCatalogExcelImportPreviewResponse,
  PreviewSingleWindowReferenceCatalogExcelImportRequest,
  SingleWindowReferenceAcdCountryEntry,
  SingleWindowReferenceAcdTradeModeEntry,
  SingleWindowReferenceCatalogModel,
  SingleWindowReferenceCountryEntry,
  SingleWindowReferenceCurrencyEntry,
  SingleWindowReferencePortEntry,
  SingleWindowReferenceTransportModeEntry,
} from "../../api/index.ts";

export type CatalogKey = keyof SingleWindowReferenceCatalogModel;
export type CatalogRow =
  | SingleWindowReferenceCountryEntry
  | SingleWindowReferenceAcdCountryEntry
  | SingleWindowReferenceCurrencyEntry
  | SingleWindowReferenceAcdTradeModeEntry
  | SingleWindowReferenceTransportModeEntry
  | SingleWindowReferencePortEntry;

export type CatalogColumn = {
  key: string;
  label: string;
  required?: boolean;
  kind?: "text" | "aliases";
};

export type CatalogCellPosition = {
  rowIndex: number;
  columnIndex: number;
};

export type CatalogPageDefinition = {
  key: CatalogKey;
  label: string;
  keyField: string;
  duplicateLabel: string;
  columns: CatalogColumn[];
  createRow: () => CatalogRow;
};

export const catalogPages: CatalogPageDefinition[] = [
  {
    key: "countries",
    label: "国家/地区(COO)",
    keyField: "code",
    duplicateLabel: "COO国家/地区代码",
    columns: [
      { key: "code", label: "代码", required: true },
      { key: "englishName", label: "英文名", required: true },
      { key: "chineseName", label: "中文名", required: true },
      { key: "aliases", label: "别名", kind: "aliases" },
    ],
    createRow: () => ({ code: "", englishName: "", chineseName: "", aliases: [] }),
  },
  {
    key: "acdCountries",
    label: "国别地区(ACD)",
    keyField: "code",
    duplicateLabel: "ACD国别地区代码",
    columns: [
      { key: "code", label: "代码", required: true },
      { key: "chineseName", label: "中文简称", required: true },
      { key: "englishName", label: "英文名", required: true },
      { key: "aliases", label: "别名", kind: "aliases" },
    ],
    createRow: () => ({ code: "", chineseName: "", englishName: "", aliases: [] }),
  },
  {
    key: "currencies",
    label: "币制",
    keyField: "code",
    duplicateLabel: "币制标准数字代码",
    columns: [
      { key: "code", label: "标准数字代码", required: true },
      { key: "acdCode", label: "ACD海关币制码" },
      { key: "alphaCode", label: "字母代码", required: true },
      { key: "aliases", label: "别名", kind: "aliases" },
    ],
    createRow: () => ({ code: "", acdCode: "", alphaCode: "", aliases: [] }),
  },
  {
    key: "acdTradeModes",
    label: "贸易方式(ACD)",
    keyField: "code",
    duplicateLabel: "ACD贸易方式代码",
    columns: [
      { key: "code", label: "代码", required: true },
      { key: "name", label: "简称", required: true },
      { key: "description", label: "说明" },
      { key: "aliases", label: "别名", kind: "aliases" },
    ],
    createRow: () => ({ code: "", name: "", description: "", aliases: [] }),
  },
  {
    key: "transportModes",
    label: "运输方式",
    keyField: "value",
    duplicateLabel: "运输方式标准值",
    columns: [
      { key: "value", label: "标准值", required: true },
      { key: "aliases", label: "别名", kind: "aliases" },
    ],
    createRow: () => ({ value: "", aliases: [] }),
  },
  {
    key: "ports",
    label: "港口",
    keyField: "value",
    duplicateLabel: "港口标准值",
    columns: [
      { key: "value", label: "标准值", required: true },
      { key: "aliases", label: "别名", kind: "aliases" },
    ],
    createRow: () => ({ value: "", aliases: [] }),
  },
];

const excelColumnQueryKeys: Record<string, string> = {
  code: "codeColumn",
  englishName: "englishNameColumn",
  chineseName: "chineseNameColumn",
  acdCode: "acdCodeColumn",
  alphaCode: "alphaCodeColumn",
  name: "nameColumn",
  description: "descriptionColumn",
  value: "valueColumn",
  aliases: "aliasesColumn",
};

export function normalizeCatalog(catalog: SingleWindowReferenceCatalogModel | null | undefined): SingleWindowReferenceCatalogModel {
  return {
    countries: (catalog?.countries ?? []).map((item) => ({ code: item.code ?? "", englishName: item.englishName ?? "", chineseName: item.chineseName ?? "", aliases: normalizeAliases(item.aliases) })),
    acdCountries: (catalog?.acdCountries ?? []).map((item) => ({ code: item.code ?? "", chineseName: item.chineseName ?? "", englishName: item.englishName ?? "", aliases: normalizeAliases(item.aliases) })),
    currencies: (catalog?.currencies ?? []).map((item) => ({ code: item.code ?? "", acdCode: item.acdCode ?? "", alphaCode: item.alphaCode ?? "", aliases: normalizeAliases(item.aliases) })),
    acdTradeModes: (catalog?.acdTradeModes ?? []).map((item) => ({ code: item.code ?? "", name: item.name ?? "", description: item.description ?? "", aliases: normalizeAliases(item.aliases) })),
    transportModes: (catalog?.transportModes ?? []).map((item) => ({ value: item.value ?? "", aliases: normalizeAliases(item.aliases) })),
    ports: (catalog?.ports ?? []).map((item) => ({ value: item.value ?? "", aliases: normalizeAliases(item.aliases) })),
  };
}

export function getRows(catalog: SingleWindowReferenceCatalogModel | null | undefined, key: CatalogKey): CatalogRow[] {
  return catalog ? [...((catalog[key] as CatalogRow[] | undefined) ?? [])] : [];
}

export function setRows(catalog: SingleWindowReferenceCatalogModel, key: CatalogKey, rows: CatalogRow[]): SingleWindowReferenceCatalogModel {
  return { ...catalog, [key]: rows } as SingleWindowReferenceCatalogModel;
}

export function cloneCatalogRow(row: CatalogRow): CatalogRow {
  return { ...(row as unknown as Record<string, unknown>), aliases: readAliases(row) } as unknown as CatalogRow;
}

export function readRowString(row: CatalogRow, key: string) {
  const value = (row as unknown as Record<string, unknown>)[key];
  return typeof value === "string" ? value : "";
}

export function readAliases(row: CatalogRow) {
  const value = (row as unknown as Record<string, unknown>).aliases;
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : [];
}

export function parseAliases(value: string) {
  return normalizeAliases(value.split(/[\n\r,，;；]+/));
}

export function parsePastedTableRows(value: string) {
  const lines = value.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
  while (lines.length > 0 && !lines[lines.length - 1].trim()) lines.pop();
  return lines.map((line) => line.split("\t"));
}

export function normalizePastedCellValue(column: CatalogColumn, value: string) {
  return column.kind === "aliases" ? parseAliases(value) : value.trim();
}

export function normalizeAliases(values: readonly string[] | undefined) {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const value of values ?? []) {
    const trimmed = value.trim();
    const key = trimmed.toLowerCase();
    if (trimmed && !seen.has(key)) { seen.add(key); result.push(trimmed); }
  }
  return result;
}

export function joinAliases(values: readonly string[]) {
  return normalizeAliases(values).join("\n");
}

export function buildExcelImportRequest(catalogKey: CatalogKey, file: File, sheetName: string, headerRowNumber: string, dataStartRowNumber: string, columnMap: Record<string, string>): Omit<PreviewSingleWindowReferenceCatalogExcelImportRequest, "body"> {
  const request: Omit<PreviewSingleWindowReferenceCatalogExcelImportRequest, "body"> = {
    catalogKey: String(catalogKey), fileName: file.name || undefined, sheetName: sheetName.trim() || undefined,
    headerRowNumber: readPositiveInteger(headerRowNumber, 1), dataStartRowNumber: readPositiveInteger(dataStartRowNumber, 2),
  };
  for (const [fieldKey, queryKey] of Object.entries(excelColumnQueryKeys)) {
    const columnNumber = readPositiveInteger(columnMap[fieldKey] ?? "", 0);
    if (columnNumber > 0) (request as Record<string, unknown>)[queryKey] = columnNumber;
  }
  return request;
}

export function buildColumnMapState(response: ApiSingleWindowReferenceCatalogExcelImportPreviewResponse) {
  const result: Record<string, string> = {};
  for (const mapping of response.columnMappings ?? []) result[mapping.fieldKey] = mapping.columnNumber > 0 ? String(mapping.columnNumber) : "";
  return result;
}

export function readPositiveInteger(value: string, fallback: number) {
  const numberValue = Number.parseInt(value, 10);
  return Number.isFinite(numberValue) && numberValue > 0 ? numberValue : fallback;
}

export function validateCatalog(catalog: SingleWindowReferenceCatalogModel) {
  const errors: string[] = [];
  for (const page of catalogPages) {
    const rows = getRows(catalog, page.key);
    for (const duplicate of findDuplicateValues(rows.map((row) => readRowString(row, page.keyField)))) errors.push(`${page.duplicateLabel}存在重复值：${duplicate}`);
    for (const [index, row] of rows.entries()) {
      const missingLabels = page.columns.filter((column) => column.required && !readRowString(row, column.key).trim()).map((column) => column.label);
      if (missingLabels.length > 0) errors.push(`${page.label}第 ${index + 1} 行缺少 ${missingLabels.join("、")}`);
    }
  }
  for (const duplicate of findDuplicateValues((catalog.currencies ?? []).map((row) => row.acdCode))) errors.push(`ACD海关币制码存在重复值：${duplicate}`);
  return errors;
}

export function findDuplicateValues(values: readonly string[]) {
  const counts = new Map<string, { value: string; count: number }>();
  for (const rawValue of values) {
    const value = rawValue.trim();
    if (!value) continue;
    const key = value.toLowerCase();
    const current = counts.get(key);
    counts.set(key, { value, count: (current?.count ?? 0) + 1 });
  }
  return [...counts.values()].filter((item) => item.count > 1).map((item) => item.value).sort((left, right) => left.localeCompare(right));
}

export function deduplicatePageRows(rows: CatalogRow[], page: CatalogPageDefinition) {
  const result: CatalogRow[] = [];
  const seen = new Map<string, number>();
  for (const row of rows) {
    const key = readRowString(row, page.keyField).trim().toLowerCase();
    if (!key || !seen.has(key)) {
      if (key) seen.set(key, result.length);
      result.push(row);
      continue;
    }
    const existingIndex = seen.get(key) ?? -1;
    if (existingIndex >= 0) result[existingIndex] = mergeRows(result[existingIndex], row, page.columns);
  }
  return result;
}

function mergeRows(left: CatalogRow, right: CatalogRow, columns: CatalogColumn[]) {
  const next = { ...left } as Record<string, unknown>;
  for (const column of columns) {
    if (column.kind === "aliases") { next.aliases = normalizeAliases([...readAliases(left), ...readAliases(right)]); continue; }
    if (!readRowString(left, column.key).trim()) {
      const rightValue = readRowString(right, column.key).trim();
      if (rightValue) next[column.key] = rightValue;
    }
  }
  return next as unknown as CatalogRow;
}
