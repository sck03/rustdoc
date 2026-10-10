import { portableReportSansFontFamily } from "../../app/typographyPolicy.ts";
import type {
  ReportBlock,
  ReportConditionalContent,
  ReportDesignerReportType,
  ReportDesignerSchema,
  ReportGridBlock,
  ReportGridCell,
  ReportGridColumn,
  ReportGridRow,
  ReportImageBlock,
  ReportRowColumn,
  ReportSection,
  ReportSectionPrintSettings,
} from "./reportDesignerSchema.ts";
import { validateReportTypeFieldDomains } from "./reportDesignerSchemaDomains.ts";
import { normalizeDetailTableBlock } from "./reportDesignerSchemaDetailTable.ts";
import { normalizeBlockOutputSettings } from "./reportDesignerSchemaBlockSettings.ts";
import { normalizeConditions } from "./reportDesignerConditions.ts";
import { reportGridCellPlacements } from "./reportDesignerGridPlacement.ts";
import { normalizeGridDiagonalHeader } from "./reportDesignerGridDiagonalValidation.ts";
import {
  createIssue,
  isRecord,
  normalizeBorderStyle,
  normalizeId,
  normalizeOptionalBorderStyle,
  normalizeTextStyle,
  readBoolean,
  readEnum,
  readNumber,
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

export type EmbeddedReportDesignerBlockValidationOptions = {
  reportType: ReportDesignerReportType;
  sectionType: ReportSection["type"];
  path: string;
  blockIds?: Set<string>;
};

/**
 * Normalize a structured block when it is embedded in a V3 Flow element.
 *
 * V3 owns the canvas geometry, but the block AST remains the single source of
 * truth for rows, grids, conditions and detail tables.  Routing embedded
 * blocks through the existing normalizer keeps all field, expression, border,
 * size and placement rules in one place instead of creating a weaker V3-only
 * validator.
 */
export function normalizeEmbeddedReportDesignerBlock(
  value: unknown,
  options: EmbeddedReportDesignerBlockValidationOptions,
): { block: ReportBlock | null; issues: ReportDesignerSchemaIssue[] } {
  const issues: ReportDesignerSchemaIssue[] = [];
  const blockIds = options.blockIds ?? new Set<string>();
  const block = normalizeBlock(value, options.path, blockIds, issues);
  if (!block) {
    return { block: null, issues };
  }

  const domainIssues: ReportDesignerSchemaIssue[] = [];
  const syntheticSchema: ReportDesignerSchema = {
    version: 2,
    reportType: options.reportType,
    page: {
      size: "A4",
      orientation: "Portrait",
      marginTopMm: 0,
      marginRightMm: 0,
      marginBottomMm: 0,
      marginLeftMm: 0,
      fontFamily: portableReportSansFontFamily,
      fontSizePt: 10,
    },
    sections: [{
      id: "embedded-section",
      type: options.sectionType,
      print: createSectionPrintDefaults(options.sectionType),
      blocks: [block],
    }],
  };
  validateReportTypeFieldDomains(syntheticSchema, domainIssues);
  const syntheticPrefix = "$.sections[0].blocks[0]";
  issues.push(...domainIssues.map((issue) => ({
    ...issue,
    path: issue.path === syntheticPrefix
      ? options.path
      : issue.path.startsWith(`${syntheticPrefix}.`)
        ? `${options.path}${issue.path.slice(syntheticPrefix.length)}`
        : issue.path,
  })));

  return { block, issues };
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
        ...normalizeConditions(value, path, issues),
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
    issues.push(createIssue("error", `${path}.columns`, "普通表格至少需要一列。"));
    return null;
  }

  if (!Array.isArray(value.rows) || rawRows.length === 0) {
    issues.push(createIssue("error", `${path}.rows`, "普通表格至少需要一行。"));
    return null;
  }

  const columnIds = new Set<string>();
  const columns = rawColumns
    .map((column, index) => normalizeGridColumn(column, `${path}.columns[${index}]`, columnIds, issues))
    .filter((column): column is ReportGridColumn => Boolean(column));
  if (columns.length === 0) {
    issues.push(createIssue("error", `${path}.columns`, "普通表格没有可用列。"));
    return null;
  }

  const rowIds = new Set<string>();
  const cellIds = new Set<string>();
  const rows = rawRows
    .map((row, index) => normalizeGridRow(row, `${path}.rows[${index}]`, rowIds, cellIds, issues))
    .filter((row): row is ReportGridRow => Boolean(row));
  if (rows.length === 0) {
    issues.push(createIssue("error", `${path}.rows`, "普通表格没有可用行。"));
    return null;
  }

  const grid: ReportGridBlock = {
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
  reportGridCellPlacements(grid, path, issues);
  return grid;
}

function normalizeGridColumn(
  value: unknown,
  path: string,
  columnIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportGridColumn | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "普通表格列必须是对象。"));
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
  path: string,
  rowIds: Set<string>,
  cellIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportGridRow | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "普通表格行必须是对象。"));
    return null;
  }

  const rawCells = Array.isArray(value.cells) ? value.cells : [];
  if (!Array.isArray(value.cells)) {
    issues.push(createIssue("error", `${path}.cells`, "普通表格单元格列表必须是数组。"));
    return null;
  }

  const cells = rawCells
    .map((cell, index) => normalizeGridCell(cell, `${path}.cells[${index}]`, cellIds, issues))
    .filter((cell): cell is ReportGridCell => Boolean(cell));

  return {
    id: normalizeId(value.id, "grid-row", rowIds, `${path}.id`, issues),
    heightMm: readOptionalNumber(value.heightMm, 9, 2, 80, `${path}.heightMm`, issues),
    cells,
  };
}

function normalizeGridCell(
  value: unknown,
  path: string,
  cellIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportGridCell | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "普通表格单元格必须是对象。"));
    return null;
  }

  const contentKind = readEnum(value.contentKind, ["Text", "Field", "CheckboxGroup"] as const, "Text", `${path}.contentKind`, issues);
  return {
    id: normalizeId(value.id, "grid-cell", cellIds, `${path}.id`, issues),
    colSpan: readGridSpan(value.colSpan, 1, 1, 1000, `${path}.colSpan`, issues),
    rowSpan: readGridSpan(value.rowSpan, 1, 1, 1000, `${path}.rowSpan`, issues),
    contentKind,
    text: readString(value.text, "", `${path}.text`, issues),
    label: readOptionalString(value.label, `${path}.label`, issues),
    fieldPath: contentKind === "Field" || contentKind === "CheckboxGroup"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    fallbackText: readOptionalString(value.fallbackText, `${path}.fallbackText`, issues),
    checkboxOptions: normalizeGridCheckboxOptions(value.checkboxOptions, `${path}.checkboxOptions`, issues),
    verticalText: readBoolean(value.verticalText, false, `${path}.verticalText`, issues),
    labelPosition: value.labelPosition === "Above" || value.labelPosition === "Prefix" ? value.labelPosition : "Inline",
    diagonalHeader: normalizeGridDiagonalHeader(value.diagonalHeader, `${path}.diagonalHeader`, issues),
    style: normalizeTextStyle(value.style, `${path}.style`, issues),
    border: normalizeOptionalBorderStyle(value.border, `${path}.border`, issues),
  };
}


function readGridSpan(
  value: unknown,
  fallback: number,
  min: number,
  max: number,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (value === undefined || value === null || value === "") return fallback;
  const parsed = typeof value === "number" ? value : typeof value === "string" ? Number.parseFloat(value) : Number.NaN;
  if (!Number.isFinite(parsed) || !Number.isInteger(parsed) || parsed < min || parsed > max) {
    issues.push(createIssue("error", path, `合并跨度和行跨度必须是 ${min}-${max} 的整数。`));
    return Number.isFinite(parsed) ? Math.min(max, Math.max(min, Math.trunc(parsed))) : fallback;
  }
  return parsed;
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

function normalizeConditionalContent(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportConditionalContent {
  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "显示内容无效，已使用固定文本。"));
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
