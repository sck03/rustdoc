import type {
  ReportBlock,
  ReportConditionalContent,
  ReportConditionalRule,
  ReportDesignerReportType,
  ReportDesignerSchema,
  ReportGridBlock,
  ReportGridCell,
  ReportGridColumn,
  ReportGridRow,
  ReportImageBlock,
  ReportPageSettings,
  ReportRowColumn,
  ReportSection,
  ReportSectionPrintSettings,
} from "./reportDesignerSchema.ts";
import { validateReportTypeFieldDomains } from "./reportDesignerSchemaDomains.ts";
import { normalizeDetailTableBlock } from "./reportDesignerSchemaDetailTable.ts";
import { normalizeBlockOutputSettings } from "./reportDesignerSchemaBlockSettings.ts";
import {
  createIssue,
  isRecord,
  normalizeBorderStyle,
  normalizeId,
  normalizeOptionalBorderStyle,
  normalizeTextStyle,
  readBoolean,
  readEnum,
  readFontFamily,
  readNumber,
  readOptionalEnum,
  readOptionalFieldPath,
  readOptionalImageSource,
  readOptionalNumber,
  readOptionalString,
  readRequiredFieldPath,
  readRequiredImageSource,
  readString,
  type ReportDesignerSchemaIssue,
} from "./reportDesignerSchemaValues.ts";

export {
  isReportDesignerCssColor,
  isReportDesignerFieldPath,
  isReportDesignerImageSource,
  isSafeReportDesignerCssFontFamily,
} from "./reportDesignerSchemaValues.ts";
export type { ReportDesignerSchemaIssue } from "./reportDesignerSchemaValues.ts";

export const CURRENT_REPORT_DESIGNER_SCHEMA_VERSION = 2;

export type ReportDesignerSchemaValidationResult = {
  schema: ReportDesignerSchema | null;
  issues: ReportDesignerSchemaIssue[];
};

export function normalizeReportDesignerSchema(input: unknown): ReportDesignerSchemaValidationResult {
  const issues: ReportDesignerSchemaIssue[] = [];
  if (!isRecord(input)) {
    return {
      schema: null,
      issues: [createIssue("error", "$", "设计器 schema 必须是对象。")],
    };
  }

  const migrated = migrateReportDesignerSchemaVersion(input, issues);
  if (!migrated) {
    return { schema: null, issues };
  }

  const reportType = normalizeReportType(migrated.reportType, issues);
  const page = normalizePageSettings(migrated.page, issues);
  const sections = normalizeSections(migrated.sections, issues);
  if (!page || !sections) {
    return { schema: null, issues };
  }

  const schema: ReportDesignerSchema = {
    version: CURRENT_REPORT_DESIGNER_SCHEMA_VERSION,
    reportType,
    page,
    sections,
  };
  validateReportTypeFieldDomains(schema, issues);

  return {
    schema,
    issues,
  };
}

export function validateReportDesignerSchema(schema: ReportDesignerSchema) {
  return normalizeReportDesignerSchema(schema).issues;
}

export function hasBlockingReportDesignerSchemaIssues(issues: ReportDesignerSchemaIssue[]) {
  return issues.some((issue) => issue.severity === "error");
}

function migrateReportDesignerSchemaVersion(
  input: Record<string, unknown>,
  issues: ReportDesignerSchemaIssue[],
): Record<string, unknown> | null {
  const version = input.version;
  if (version === CURRENT_REPORT_DESIGNER_SCHEMA_VERSION) {
    return input;
  }

  if (version === 1 || version === 0 || version === undefined) {
    issues.push(createIssue("warning", "$.version", "已按 v2 schema 兼容读取旧草稿结构。"));
    return {
      ...input,
      version: CURRENT_REPORT_DESIGNER_SCHEMA_VERSION,
      sections: migrateLegacySectionsToV2(input.sections),
    };
  }

  issues.push(createIssue("error", "$.version", `暂不支持 schema version ${String(version)}。`));
  return null;
}

function migrateLegacySectionsToV2(value: unknown) {
  if (!Array.isArray(value)) {
    return value;
  }

  return value.map((section) => {
    if (!isRecord(section) || section.print !== undefined) {
      return section;
    }

    const sectionType = section.type === "Header" || section.type === "Body" || section.type === "Footer"
      ? section.type
      : "Body";

    return {
      ...section,
      print: createLegacySectionPrintDefaults(sectionType),
    };
  });
}

function normalizeReportType(value: unknown, issues: ReportDesignerSchemaIssue[]): ReportDesignerReportType {
  if (value === "ExportDocument" || value === "PaymentVoucher") {
    return value;
  }

  issues.push(createIssue("warning", "$.reportType", "报表类型无效，已回退为出口单据。"));
  return "ExportDocument";
}

function normalizePageSettings(value: unknown, issues: ReportDesignerSchemaIssue[]): ReportPageSettings | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", "$.page", "缺少页面设置。"));
    return null;
  }

  const size = readEnum(value.size, ["A4", "A5", "Letter", "Custom"] as const, "A4", "$.page.size", issues);
  const orientation = readEnum(value.orientation, ["Portrait", "Landscape"] as const, "Portrait", "$.page.orientation", issues);
  const page: ReportPageSettings = {
    size,
    orientation,
    marginTopMm: readNumber(value.marginTopMm, 16, 0, 80, "$.page.marginTopMm", issues),
    marginRightMm: readNumber(value.marginRightMm, 14, 0, 80, "$.page.marginRightMm", issues),
    marginBottomMm: readNumber(value.marginBottomMm, 16, 0, 80, "$.page.marginBottomMm", issues),
    marginLeftMm: readNumber(value.marginLeftMm, 14, 0, 80, "$.page.marginLeftMm", issues),
    fontFamily: readFontFamily(value.fontFamily, "$.page.fontFamily", issues),
    fontSizePt: readNumber(value.fontSizePt, 10, 6, 48, "$.page.fontSizePt", issues),
  };

  if (size === "Custom") {
    page.widthMm = readNumber(value.widthMm, 210, 40, 600, "$.page.widthMm", issues);
    page.heightMm = readNumber(value.heightMm, 297, 40, 600, "$.page.heightMm", issues);
  }

  return page;
}

function normalizeSections(value: unknown, issues: ReportDesignerSchemaIssue[]): ReportSection[] | null {
  if (!Array.isArray(value) || value.length === 0) {
    issues.push(createIssue("error", "$.sections", "schema 至少需要一个版区。"));
    return null;
  }

  const sectionIds = new Set<string>();
  const blockIds = new Set<string>();
  const sections = value
    .map((section, index) => normalizeSection(section, index, sectionIds, blockIds, issues))
    .filter((section): section is ReportSection => Boolean(section));

  if (sections.length === 0) {
    issues.push(createIssue("error", "$.sections", "没有可用的版区。"));
    return null;
  }

  return sections;
}

function normalizeSection(
  value: unknown,
  index: number,
  sectionIds: Set<string>,
  blockIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportSection | null {
  const path = `$.sections[${index}]`;
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "版区必须是对象。"));
    return null;
  }

  if (value.type !== "Header" && value.type !== "Body" && value.type !== "Footer") {
    issues.push(createIssue("error", `${path}.type`, "版区类型无效。"));
    return null;
  }

  const blocksValue = value.blocks;
  if (!Array.isArray(blocksValue)) {
    issues.push(createIssue("error", `${path}.blocks`, "版区组件列表必须是数组。"));
    return null;
  }

  return {
    id: normalizeId(value.id, `section-${value.type.toLowerCase()}`, sectionIds, `${path}.id`, issues),
    type: value.type,
    print: normalizeSectionPrintSettings(value.print, value.type, `${path}.print`, issues),
    blocks: blocksValue
      .map((block, blockIndex) => normalizeBlock(block, `${path}.blocks[${blockIndex}]`, blockIds, issues))
      .filter((block): block is ReportBlock => Boolean(block)),
  };
}

function normalizeSectionPrintSettings(
  value: unknown,
  sectionType: ReportSection["type"],
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportSectionPrintSettings {
  const fallback = createSectionPrintDefaults(sectionType);
  if (value === undefined || value === null) {
    return fallback;
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "版区打印设置无效，已使用默认设置。"));
    return fallback;
  }

  const requestedRepeat = readBoolean(value.repeatOnEveryPage, fallback.repeatOnEveryPage, `${path}.repeatOnEveryPage`, issues);
  if (sectionType === "Body" && requestedRepeat) {
    issues.push(createIssue("warning", `${path}.repeatOnEveryPage`, "主体版区不支持跨页重复，已关闭该设置。"));
  }

  const minHeightMm = readOptionalNumber(value.minHeightMm, fallback.minHeightMm ?? 0, 0, 260, `${path}.minHeightMm`, issues);
  const print: ReportSectionPrintSettings = {
    repeatOnEveryPage: sectionType === "Body" ? false : requestedRepeat,
    keepTogether: readBoolean(value.keepTogether, fallback.keepTogether, `${path}.keepTogether`, issues),
    pinToPageBottom: sectionType === "Footer"
      ? readBoolean(value.pinToPageBottom, fallback.pinToPageBottom ?? false, `${path}.pinToPageBottom`, issues)
      : false,
  };

  if (minHeightMm !== undefined) {
    print.minHeightMm = minHeightMm;
  }

  return print;
}

function createSectionPrintDefaults(sectionType: ReportSection["type"]): ReportSectionPrintSettings {
  if (sectionType === "Body") {
    return {
      repeatOnEveryPage: false,
      keepTogether: false,
      pinToPageBottom: false,
    };
  }

  return {
    repeatOnEveryPage: true,
    keepTogether: true,
    pinToPageBottom: sectionType === "Footer",
  };
}

function createLegacySectionPrintDefaults(sectionType: ReportSection["type"]): ReportSectionPrintSettings {
  return {
    repeatOnEveryPage: false,
    keepTogether: sectionType !== "Body",
    pinToPageBottom: false,
  };
}

function normalizeBlock(
  value: unknown,
  path: string,
  blockIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportBlock | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "组件必须是对象。"));
    return null;
  }

  switch (value.type) {
    case "Text":
      return {
        id: normalizeId(value.id, "block-text", blockIds, `${path}.id`, issues),
        type: "Text",
        output: normalizeBlockOutputSettings(value.output, `${path}.output`, issues),
        text: readString(value.text, "", `${path}.text`, issues),
        style: normalizeTextStyle(value.style, `${path}.style`, issues),
        border: normalizeOptionalBorderStyle(value.border, `${path}.border`, issues),
      };
    case "Field":
      return {
        id: normalizeId(value.id, "block-field", blockIds, `${path}.id`, issues),
        type: "Field",
        output: normalizeBlockOutputSettings(value.output, `${path}.output`, issues),
        label: readOptionalString(value.label, `${path}.label`, issues),
        fieldPath: readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
        fallbackText: readOptionalString(value.fallbackText, `${path}.fallbackText`, issues),
        style: normalizeTextStyle(value.style, `${path}.style`, issues),
        border: normalizeOptionalBorderStyle(value.border, `${path}.border`, issues),
      };
    case "Row":
      return normalizeRowBlock(value, path, blockIds, issues);
    case "Grid":
      return normalizeGridBlock(value, path, blockIds, issues);
    case "Conditional":
      return {
        id: normalizeId(value.id, "block-conditional", blockIds, `${path}.id`, issues),
        type: "Conditional",
        output: normalizeBlockOutputSettings(value.output, `${path}.output`, issues),
        condition: normalizeConditionalRule(value.condition, `${path}.condition`, issues),
        content: normalizeConditionalContent(value.content, `${path}.content`, issues),
        style: normalizeTextStyle(value.style, `${path}.style`, issues),
        border: normalizeOptionalBorderStyle(value.border, `${path}.border`, issues),
      };
    case "Image":
      return normalizeImageBlock(value, path, blockIds, issues);
    case "DetailTable":
      return normalizeDetailTableBlock(value, path, blockIds, issues);
    case "PageBreak":
      return {
        id: normalizeId(value.id, "block-page-break", blockIds, `${path}.id`, issues),
        type: "PageBreak",
        output: normalizeBlockOutputSettings(value.output, `${path}.output`, issues),
      };
    default:
      issues.push(createIssue("error", `${path}.type`, `不支持的组件类型 ${String(value.type)}。`));
      return null;
  }
}

function normalizeImageBlock(
  value: Record<string, unknown>,
  path: string,
  blockIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportImageBlock {
  const sourceKind = readEnum(value.sourceKind, ["Field", "StaticUrl"] as const, "Field", `${path}.sourceKind`, issues);

  return {
    id: normalizeId(value.id, "block-image", blockIds, `${path}.id`, issues),
    type: "Image",
    output: normalizeBlockOutputSettings(value.output, `${path}.output`, issues),
    title: readOptionalString(value.title, `${path}.title`, issues),
    sourceKind,
    fieldPath: sourceKind === "Field"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    url: sourceKind === "StaticUrl"
      ? readRequiredImageSource(value.url, `${path}.url`, issues)
      : readOptionalImageSource(value.url, `${path}.url`, issues),
    altText: readOptionalString(value.altText, `${path}.altText`, issues),
    widthMm: readNumber(value.widthMm, 42, 4, 180, `${path}.widthMm`, issues),
    heightMm: readOptionalNumber(value.heightMm, 24, 4, 180, `${path}.heightMm`, issues),
    align: readEnum(value.align, ["Left", "Center", "Right"] as const, "Right", `${path}.align`, issues),
    marginTopMm: readOptionalNumber(value.marginTopMm, 0, 0, 80, `${path}.marginTopMm`, issues),
    marginBottomMm: readOptionalNumber(value.marginBottomMm, 0, 0, 80, `${path}.marginBottomMm`, issues),
    hideWhenSourceEmpty: readBoolean(value.hideWhenSourceEmpty, true, `${path}.hideWhenSourceEmpty`, issues),
    keepTogether: readBoolean(value.keepTogether, true, `${path}.keepTogether`, issues),
  };
}

function normalizeRowBlock(
  value: Record<string, unknown>,
  path: string,
  blockIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportBlock | null {
  const rawColumns = Array.isArray(value.columns) ? value.columns : [];
  if (!Array.isArray(value.columns)) {
    issues.push(createIssue("error", `${path}.columns`, "行组件至少需要一列。"));
    return null;
  }

  if (rawColumns.length === 0) {
    issues.push(createIssue("error", `${path}.columns`, "行组件至少需要一列。"));
    return null;
  }

  const columnIds = new Set<string>();
  const columns = rawColumns
    .map((column, index) => normalizeRowColumn(column, `${path}.columns[${index}]`, columnIds, issues))
    .filter((column): column is ReportRowColumn => Boolean(column));

  if (columns.length === 0) {
    issues.push(createIssue("error", `${path}.columns`, "行组件没有可用列。"));
    return null;
  }

  return {
    id: normalizeId(value.id, "block-row", blockIds, `${path}.id`, issues),
    type: "Row",
    output: normalizeBlockOutputSettings(value.output, `${path}.output`, issues),
    columns: normalizeRowColumnWidthsForValidation(columns),
    marginTopMm: readOptionalNumber(value.marginTopMm, 0, 0, 80, `${path}.marginTopMm`, issues),
    marginBottomMm: readOptionalNumber(value.marginBottomMm, 0, 0, 80, `${path}.marginBottomMm`, issues),
  };
}

function normalizeRowColumn(
  value: unknown,
  path: string,
  columnIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportRowColumn | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "行列必须是对象。"));
    return null;
  }

  const contentKind = readEnum(value.contentKind, ["Text", "Field"] as const, "Text", `${path}.contentKind`, issues);

  return {
    id: normalizeId(value.id, "row-col", columnIds, `${path}.id`, issues),
    contentKind,
    text: readString(value.text, "", `${path}.text`, issues),
    label: readOptionalString(value.label, `${path}.label`, issues),
    fieldPath: contentKind === "Field"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    fallbackText: readOptionalString(value.fallbackText, `${path}.fallbackText`, issues),
    widthPercent: readNumber(value.widthPercent, 50, 1, 100, `${path}.widthPercent`, issues),
    style: normalizeTextStyle(value.style, `${path}.style`, issues),
    border: normalizeOptionalBorderStyle(value.border, `${path}.border`, issues),
  };
}

function normalizeRowColumnWidthsForValidation(columns: ReportRowColumn[]) {
  const total = columns.reduce((sum, column) => sum + Math.max(1, column.widthPercent), 0);
  return columns.map((column) => ({
    ...column,
    widthPercent: Math.round((Math.max(1, column.widthPercent) / total) * 1000) / 10,
  }));
}

function normalizeGridBlock(
  value: Record<string, unknown>,
  path: string,
  blockIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportGridBlock | null {
  const rawColumns = Array.isArray(value.columns) ? value.columns : [];
  const rawRows = Array.isArray(value.rows) ? value.rows : [];
  if (!Array.isArray(value.columns) || rawColumns.length === 0) {
    issues.push(createIssue("error", `${path}.columns`, "票据表格至少需要一列。"));
    return null;
  }

  if (!Array.isArray(value.rows) || rawRows.length === 0) {
    issues.push(createIssue("error", `${path}.rows`, "票据表格至少需要一行。"));
    return null;
  }

  const columnIds = new Set<string>();
  const columns = rawColumns
    .map((column, index) => normalizeGridColumn(column, `${path}.columns[${index}]`, columnIds, issues))
    .filter((column): column is ReportGridColumn => Boolean(column));
  if (columns.length === 0) {
    issues.push(createIssue("error", `${path}.columns`, "票据表格没有可用列。"));
    return null;
  }

  const rowIds = new Set<string>();
  const rows = rawRows
    .map((row, index) => normalizeGridRow(row, columns.length, `${path}.rows[${index}]`, rowIds, issues))
    .filter((row): row is ReportGridRow => Boolean(row));
  if (rows.length === 0) {
    issues.push(createIssue("error", `${path}.rows`, "票据表格没有可用行。"));
    return null;
  }

  return {
    id: normalizeId(value.id, "block-grid", blockIds, `${path}.id`, issues),
    type: "Grid",
    output: normalizeBlockOutputSettings(value.output, `${path}.output`, issues),
    title: readOptionalString(value.title, `${path}.title`, issues),
    columns: normalizeGridColumnWidths(columns),
    rows,
    marginTopMm: readOptionalNumber(value.marginTopMm, 0, 0, 80, `${path}.marginTopMm`, issues),
    marginBottomMm: readOptionalNumber(value.marginBottomMm, 0, 0, 80, `${path}.marginBottomMm`, issues),
    border: normalizeBorderStyle(value.border, `${path}.border`, issues),
    defaultCellStyle: normalizeTextStyle(value.defaultCellStyle, `${path}.defaultCellStyle`, issues),
  };
}

function normalizeGridColumn(
  value: unknown,
  path: string,
  columnIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportGridColumn | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "票据表格列必须是对象。"));
    return null;
  }

  return {
    id: normalizeId(value.id, "grid-col", columnIds, `${path}.id`, issues),
    widthPercent: readNumber(value.widthPercent, 10, 1, 100, `${path}.widthPercent`, issues),
  };
}

function normalizeGridColumnWidths(columns: ReportGridColumn[]) {
  const total = columns.reduce((sum, column) => sum + Math.max(1, column.widthPercent), 0);
  return columns.map((column) => ({
    ...column,
    widthPercent: Math.round((Math.max(1, column.widthPercent) / total) * 1000) / 10,
  }));
}

function normalizeGridRow(
  value: unknown,
  columnCount: number,
  path: string,
  rowIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportGridRow | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "票据表格行必须是对象。"));
    return null;
  }

  const rawCells = Array.isArray(value.cells) ? value.cells : [];
  if (!Array.isArray(value.cells) || rawCells.length === 0) {
    issues.push(createIssue("error", `${path}.cells`, "票据表格行至少需要一个单元格。"));
    return null;
  }

  const cellIds = new Set<string>();
  const cells = rawCells
    .map((cell, index) => normalizeGridCell(cell, columnCount, `${path}.cells[${index}]`, cellIds, issues))
    .filter((cell): cell is ReportGridCell => Boolean(cell));
  if (cells.length === 0) {
    issues.push(createIssue("error", `${path}.cells`, "票据表格行没有可用单元格。"));
    return null;
  }

  return {
    id: normalizeId(value.id, "grid-row", rowIds, `${path}.id`, issues),
    heightMm: readOptionalNumber(value.heightMm, 9, 2, 80, `${path}.heightMm`, issues),
    cells,
  };
}

function normalizeGridCell(
  value: unknown,
  columnCount: number,
  path: string,
  cellIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportGridCell | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "票据表格单元格必须是对象。"));
    return null;
  }

  const contentKind = readEnum(value.contentKind, ["Text", "Field", "CheckboxGroup"] as const, "Text", `${path}.contentKind`, issues);
  return {
    id: normalizeId(value.id, "grid-cell", cellIds, `${path}.id`, issues),
    colSpan: Math.floor(readNumber(value.colSpan, 1, 1, Math.max(1, columnCount), `${path}.colSpan`, issues)),
    rowSpan: Math.floor(readNumber(value.rowSpan, 1, 1, 80, `${path}.rowSpan`, issues)),
    contentKind,
    text: readString(value.text, "", `${path}.text`, issues),
    label: readOptionalString(value.label, `${path}.label`, issues),
    fieldPath: contentKind === "Field" || contentKind === "CheckboxGroup"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    fallbackText: readOptionalString(value.fallbackText, `${path}.fallbackText`, issues),
    checkboxOptions: normalizeGridCheckboxOptions(value.checkboxOptions, `${path}.checkboxOptions`, issues),
    verticalText: readBoolean(value.verticalText, false, `${path}.verticalText`, issues),
    style: normalizeTextStyle(value.style, `${path}.style`, issues),
    border: normalizeOptionalBorderStyle(value.border, `${path}.border`, issues),
  };
}

function normalizeGridCheckboxOptions(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (value === undefined || value === null) {
    return [];
  }

  if (!Array.isArray(value)) {
    issues.push(createIssue("warning", path, "勾选项必须是数组，已使用空列表。"));
    return [];
  }

  const optionIds = new Set<string>();
  return value
    .map((option, index) => {
      if (!isRecord(option)) {
        issues.push(createIssue("warning", `${path}[${index}]`, "勾选项无效，已忽略。"));
        return null;
      }

      return {
        id: normalizeId(option.id, "grid-option", optionIds, `${path}[${index}].id`, issues),
        label: readString(option.label, "", `${path}[${index}].label`, issues),
        value: readString(option.value, "", `${path}[${index}].value`, issues),
      };
    })
    .filter((option): option is NonNullable<typeof option> => Boolean(option));
}

function normalizeConditionalRule(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportConditionalRule {
  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "条件设置无效，已使用默认条件。"));
    return {
      fieldPath: "Invoice.SpecialTerms",
      operator: "HasValue",
      value: "",
    };
  }

  const operator = readEnum(value.operator, ["HasValue", "Equals", "NotEquals"] as const, "HasValue", `${path}.operator`, issues);

  return {
    fieldPath: readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    operator,
    value: readString(value.value, "", `${path}.value`, issues),
  };
}

function normalizeConditionalContent(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportConditionalContent {
  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "条件内容无效，已使用固定文本。"));
    return {
      kind: "Text",
      text: "",
      fieldPath: "",
    };
  }

  const kind = readEnum(value.kind, ["Text", "Field"] as const, "Text", `${path}.kind`, issues);

  return {
    kind,
    text: readString(value.text, "", `${path}.text`, issues),
    label: readOptionalString(value.label, `${path}.label`, issues),
    fieldPath: kind === "Field"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    fallbackText: readOptionalString(value.fallbackText, `${path}.fallbackText`, issues),
  };
}
