import type {
  ReportBlock,
  ReportBlockOutputSettings,
  ReportBorderStyle,
  ReportConditionalContent,
  ReportConditionalRule,
  ReportDesignerReportType,
  ReportDesignerSchema,
  ReportDetailTableBlock,
  ReportDetailTableCellContent,
  ReportDetailTableColumn,
  ReportDetailTableGroupFooter,
  ReportDetailTableGroupFooterCell,
  ReportDetailTableGrouping,
  ReportDetailTablePrintSettings,
  ReportDetailTableSideBand,
  ReportDetailTableSummaryCell,
  ReportDetailTableSummaryRow,
  ReportGridBlock,
  ReportGridCell,
  ReportGridColumn,
  ReportGridRow,
  ReportImageBlock,
  ReportPageSettings,
  ReportRowColumn,
  ReportSection,
  ReportSectionPrintSettings,
  ReportTextStyle,
} from "./reportDesignerSchema.ts";
import { getReportDesignerBlockPlacementIssue } from "./reportDesignerModel.ts";
import { portableReportSansFontFamily } from "../../app/typographyPolicy.ts";

export const CURRENT_REPORT_DESIGNER_SCHEMA_VERSION = 2;

export type ReportDesignerSchemaIssue = {
  severity: "error" | "warning";
  path: string;
  message: string;
};

export type ReportDesignerSchemaValidationResult = {
  schema: ReportDesignerSchema | null;
  issues: ReportDesignerSchemaIssue[];
};

const fieldPathPattern = /^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$/;
const cssColorPattern = /^#[0-9a-fA-F]{3,8}$/;
const safeCssFontFamilyPattern = /^[A-Za-z0-9\s"',._-]+$/;
const imageDataUrlPattern = /^data:image\/(?:png|jpe?g|gif|webp|svg\+xml);base64,[A-Za-z0-9+/=\s]+$/i;
const imageRemoteUrlPattern = /^https?:\/\/[^\s"'<>]+$/i;
const imageRelativeUrlPattern = /^(?!.*(?:^|\/)\.\.(?:\/|$))(?![a-z][a-z0-9+.-]*:)[A-Za-z0-9][A-Za-z0-9._~/%+-]*$/i;

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

export function isReportDesignerFieldPath(value: string) {
  return fieldPathPattern.test(value.trim());
}

export function isReportDesignerImageSource(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return false;
  }

  return imageDataUrlPattern.test(trimmed) ||
    imageRemoteUrlPattern.test(trimmed) ||
    imageRelativeUrlPattern.test(trimmed);
}

export function isSafeReportDesignerCssFontFamily(value: string) {
  return safeCssFontFamilyPattern.test(value.trim());
}

export function isReportDesignerCssColor(value: string) {
  return cssColorPattern.test(value.trim());
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

function validateReportTypeFieldDomains(schema: ReportDesignerSchema, issues: ReportDesignerSchemaIssue[]) {
  schema.sections.forEach((section, sectionIndex) => {
    section.blocks.forEach((block, blockIndex) => {
      const blockPath = `$.sections[${sectionIndex}].blocks[${blockIndex}]`;
      const placementIssue = getReportDesignerBlockPlacementIssue(block, section);
      if (placementIssue) {
        issues.push(createIssue("error", blockPath, placementIssue));
      }

      switch (block.type) {
        case "Field":
          validateFieldPathForReportType(schema.reportType, block.fieldPath, `${blockPath}.fieldPath`, issues);
          break;
        case "Row":
          block.columns.forEach((column, columnIndex) => {
            if (column.contentKind === "Field") {
              validateFieldPathForReportType(schema.reportType, column.fieldPath, `${blockPath}.columns[${columnIndex}].fieldPath`, issues);
            }
          });
          break;
        case "Grid":
          block.rows.forEach((row, rowIndex) => {
            row.cells.forEach((cell, cellIndex) => {
              if (cell.contentKind === "Field" || cell.contentKind === "CheckboxGroup") {
                validateFieldPathForReportType(schema.reportType, cell.fieldPath, `${blockPath}.rows[${rowIndex}].cells[${cellIndex}].fieldPath`, issues);
              }
            });
          });
          break;
        case "Conditional":
          validateFieldPathForReportType(schema.reportType, block.condition.fieldPath, `${blockPath}.condition.fieldPath`, issues);
          if (block.content.kind === "Field") {
            validateFieldPathForReportType(schema.reportType, block.content.fieldPath, `${blockPath}.content.fieldPath`, issues);
          }
          break;
        case "Image":
          if (block.sourceKind === "Field") {
            validateFieldPathForReportType(schema.reportType, block.fieldPath, `${blockPath}.fieldPath`, issues);
          }
          break;
        case "DetailTable":
          if (schema.reportType === "PaymentVoucher") {
            issues.push(createIssue("error", blockPath, "付款/报销模板不能使用出口单据明细表；请用多列行组合付款或费用表格。"));
          }
          if (block.grouping) {
            validateFieldPathForReportType(schema.reportType, block.grouping.fieldPath, `${blockPath}.grouping.fieldPath`, issues);
            block.grouping.footer?.cells.forEach((cell, cellIndex) => {
              if (cell.contentKind === "Sum") {
                validateFieldPathForReportType(schema.reportType, cell.fieldPath, `${blockPath}.grouping.footer.cells[${cellIndex}].fieldPath`, issues);
                validateDetailTableItemFieldPath(cell.fieldPath, `${blockPath}.grouping.footer.cells[${cellIndex}].fieldPath`, issues);
              }
            });
          }
          block.columns.forEach((column, columnIndex) => {
            validateFieldPathForReportType(schema.reportType, column.fieldPath, `${blockPath}.columns[${columnIndex}].fieldPath`, issues);
            column.content?.forEach((part, partIndex) => {
              if (part.kind === "Field") {
                validateFieldPathForReportType(schema.reportType, part.fieldPath, `${blockPath}.columns[${columnIndex}].content[${partIndex}].fieldPath`, issues);
              }
            });
          });
          block.summaryRow?.cells.forEach((cell, cellIndex) => {
            if (cell.contentKind === "Field") {
              validateFieldPathForReportType(schema.reportType, cell.fieldPath, `${blockPath}.summaryRow.cells[${cellIndex}].fieldPath`, issues);
            }
          });
          if (block.sideBand?.contentKind === "Field") {
            validateFieldPathForReportType(schema.reportType, block.sideBand.fieldPath, `${blockPath}.sideBand.fieldPath`, issues);
          }
          break;
        case "Text":
        case "PageBreak":
          break;
      }
    });
  });
}

function validateFieldPathForReportType(
  reportType: ReportDesignerReportType,
  fieldPath: string,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (!fieldPath) {
    return;
  }

  if (isTemplateSystemFieldForReportType(reportType, fieldPath)) {
    return;
  }

  if (reportType === "PaymentVoucher") {
    if (fieldPath === "cny_amount_upper" || fieldPath.startsWith("Payment.")) {
      return;
    }

    issues.push(createIssue("error", path, "付款/报销模板只能使用 Payment.*、金额换算或模板系统字段，不能混用出口单据字段。"));
    return;
  }

  if (
    fieldPath.startsWith("Invoice.") ||
    fieldPath.startsWith("Customer.") ||
    fieldPath.startsWith("Exporter.") ||
    fieldPath.startsWith("item.")
  ) {
    return;
  }

  issues.push(createIssue("error", path, "出口单据模板只能使用 Invoice/Customer/Exporter/item 或模板系统字段。"));
}

function validateDetailTableItemFieldPath(
  fieldPath: string,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (!fieldPath) {
    return;
  }

  if (fieldPath.startsWith("Invoice.Items.") || fieldPath.startsWith("item.")) {
    return;
  }

  issues.push(createIssue("error", path, "分组小计求和字段必须来自商品明细 item.* 或 Invoice.Items.*，不能使用发票表头字段。"));
}

function isTemplateSystemFieldForReportType(reportType: ReportDesignerReportType, fieldPath: string) {
  if (fieldPath === "ShowSeal" || fieldPath === "doc_seal_path") {
    return true;
  }

  return reportType === "ExportDocument" &&
    (fieldPath === "customs_seal_path" || fieldPath === "shipping_marks_image_data");
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

function normalizeBlockOutputSettings(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportBlockOutputSettings {
  if (value === undefined || value === null) {
    return {
      enabled: true,
    };
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "组件输出设置无效，已使用默认设置。"));
    return {
      enabled: true,
    };
  }

  const note = readOptionalString(value.note, `${path}.note`, issues);
  return {
    enabled: readBoolean(value.enabled, true, `${path}.enabled`, issues),
    note: note?.slice(0, 500),
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

function normalizeDetailTableBlock(
  value: Record<string, unknown>,
  path: string,
  blockIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableBlock | null {
  if (!Array.isArray(value.columns) || value.columns.length === 0) {
    issues.push(createIssue("error", `${path}.columns`, "明细表至少需要一列。"));
    return null;
  }

  const columnIds = new Set<string>();
  const columns = value.columns
    .map((column, index) => normalizeDetailTableColumn(column, `${path}.columns[${index}]`, columnIds, issues))
    .filter((column): column is ReportDetailTableColumn => Boolean(column));
  if (columns.length === 0) {
    issues.push(createIssue("error", `${path}.columns`, "明细表没有可用列。"));
    return null;
  }

  const sourcePath = value.sourcePath === "Invoice.Items" ? "Invoice.Items" : "Invoice.Items";
  if (value.sourcePath !== "Invoice.Items") {
    issues.push(createIssue("warning", `${path}.sourcePath`, "明细表数据源已回退为 Invoice.Items。"));
  }

  const repeatMode = value.repeatMode === "ScribanFor" ? "ScribanFor" : "ScribanFor";
  if (value.repeatMode !== "ScribanFor") {
    issues.push(createIssue("warning", `${path}.repeatMode`, "明细表循环模式已回退为 ScribanFor。"));
  }

  const block: ReportDetailTableBlock = {
    id: normalizeId(value.id, "block-detail-table", blockIds, `${path}.id`, issues),
    type: "DetailTable",
    output: normalizeBlockOutputSettings(value.output, `${path}.output`, issues),
    title: readOptionalString(value.title, `${path}.title`, issues),
    detailWidthMm: readOptionalNumber(value.detailWidthMm, 132, 40, 240, `${path}.detailWidthMm`, issues),
    sourcePath,
    repeatMode,
    print: normalizeDetailTablePrintSettings(value.print, `${path}.print`, issues),
    sideBand: normalizeDetailTableSideBand(value.sideBand, `${path}.sideBand`, issues),
    grouping: normalizeDetailTableGrouping(value.grouping, columns, `${path}.grouping`, issues),
    columns,
    summaryRow: normalizeDetailTableSummaryRow(value.summaryRow, columns, `${path}.summaryRow`, issues),
    headerStyle: normalizeTextStyle(value.headerStyle, `${path}.headerStyle`, issues),
    bodyStyle: normalizeTextStyle(value.bodyStyle, `${path}.bodyStyle`, issues),
    border: normalizeBorderStyle(value.border, `${path}.border`, issues),
  };

  if (!block.sideBand) {
    delete block.detailWidthMm;
  }

  return block;
}

function normalizeDetailTablePrintSettings(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTablePrintSettings {
  if (value === undefined || value === null) {
    return {
      repeatHeaderOnPageBreak: true,
      keepRowsTogether: true,
    };
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "明细表打印设置无效，已使用默认设置。"));
    return {
      repeatHeaderOnPageBreak: true,
      keepRowsTogether: true,
    };
  }

  return {
    repeatHeaderOnPageBreak: readBoolean(value.repeatHeaderOnPageBreak, true, `${path}.repeatHeaderOnPageBreak`, issues),
    keepRowsTogether: readBoolean(value.keepRowsTogether, true, `${path}.keepRowsTogether`, issues),
  };
}

function normalizeDetailTableGrouping(
  value: unknown,
  columns: ReportDetailTableColumn[],
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableGrouping | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "明细表分组设置无效，已忽略。"));
    return undefined;
  }

  return {
    fieldPath: readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    label: readString(value.label, "Group", `${path}.label`, issues),
    showFieldValue: readBoolean(value.showFieldValue, true, `${path}.showFieldValue`, issues),
    keepTogether: readBoolean(value.keepTogether, true, `${path}.keepTogether`, issues),
    pageBreakBefore: readBoolean(value.pageBreakBefore, false, `${path}.pageBreakBefore`, issues),
    footer: normalizeDetailTableGroupFooter(value.footer, columns, `${path}.footer`, issues),
    style: normalizeTextStyle(value.style, `${path}.style`, issues),
  };
}

function normalizeDetailTableGroupFooter(
  value: unknown,
  columns: ReportDetailTableColumn[],
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableGroupFooter | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "分组小计行无效，已忽略。"));
    return undefined;
  }

  const columnIds = new Set(columns.map((column) => column.id));
  const rawCells = Array.isArray(value.cells) ? value.cells : [];
  if (!Array.isArray(value.cells)) {
    issues.push(createIssue("warning", `${path}.cells`, "分组小计单元格必须是数组，已使用空单元格。"));
  }

  const cells = rawCells
    .map((cell, index) => normalizeDetailTableGroupFooterCell(cell, `${path}.cells[${index}]`, columnIds, issues))
    .filter((cell): cell is ReportDetailTableGroupFooterCell => Boolean(cell));

  return {
    label: readString(value.label, "SUBTOTAL", `${path}.label`, issues),
    labelColumnSpan: Math.floor(readNumber(value.labelColumnSpan, Math.max(1, columns.length - 1), 1, columns.length, `${path}.labelColumnSpan`, issues)),
    cells,
    style: normalizeTextStyle(value.style, `${path}.style`, issues),
  };
}

function normalizeDetailTableGroupFooterCell(
  value: unknown,
  path: string,
  columnIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableGroupFooterCell | null {
  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "分组小计单元格无效，已忽略。"));
    return null;
  }

  const columnId = typeof value.columnId === "string" ? value.columnId.trim() : "";
  if (!columnIds.has(columnId)) {
    issues.push(createIssue("warning", `${path}.columnId`, "分组小计单元格指向的列不存在，已忽略。"));
    return null;
  }

  const contentKind = readEnum(value.contentKind, ["Empty", "Text", "Sum", "Count"] as const, "Empty", `${path}.contentKind`, issues);

  return {
    columnId,
    contentKind,
    text: contentKind === "Text" ? readString(value.text, "", `${path}.text`, issues) : readOptionalString(value.text, `${path}.text`, issues) ?? "",
    fieldPath: contentKind === "Sum"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
  };
}

function normalizeDetailTableSummaryRow(
  value: unknown,
  columns: ReportDetailTableColumn[],
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableSummaryRow | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "明细表合计行无效，已忽略。"));
    return undefined;
  }

  const columnIds = new Set(columns.map((column) => column.id));
  const rawCells = Array.isArray(value.cells) ? value.cells : [];
  if (!Array.isArray(value.cells)) {
    issues.push(createIssue("warning", `${path}.cells`, "明细表合计单元格必须是数组，已使用空单元格。"));
  }

  const cells = rawCells
    .map((cell, index) => normalizeDetailTableSummaryCell(cell, `${path}.cells[${index}]`, columnIds, issues))
    .filter((cell): cell is ReportDetailTableSummaryCell => Boolean(cell));

  return {
    label: readString(value.label, "TOTAL", `${path}.label`, issues),
    labelColumnSpan: Math.floor(readNumber(value.labelColumnSpan, Math.max(1, columns.length - 1), 1, columns.length, `${path}.labelColumnSpan`, issues)),
    cells,
    style: normalizeTextStyle(value.style, `${path}.style`, issues),
  };
}

function normalizeDetailTableSummaryCell(
  value: unknown,
  path: string,
  columnIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableSummaryCell | null {
  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "明细表合计单元格无效，已忽略。"));
    return null;
  }

  const columnId = typeof value.columnId === "string" ? value.columnId.trim() : "";
  if (!columnIds.has(columnId)) {
    issues.push(createIssue("warning", `${path}.columnId`, "明细表合计单元格指向的列不存在，已忽略。"));
    return null;
  }

  const contentKind = readEnum(value.contentKind, ["Empty", "Text", "Field"] as const, "Empty", `${path}.contentKind`, issues);

  return {
    columnId,
    contentKind,
    text: readString(value.text, "", `${path}.text`, issues),
    fieldPath: contentKind === "Field"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
  };
}

function normalizeDetailTableSideBand(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableSideBand | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "非循环侧栏无效，已忽略。"));
    return undefined;
  }

  const contentKind = readEnum(value.contentKind, ["Text", "Field"] as const, "Field", `${path}.contentKind`, issues);
  return {
    title: readString(value.title, "唛头 Marks", `${path}.title`, issues),
    widthMm: readNumber(value.widthMm, 36, 16, 120, `${path}.widthMm`, issues),
    contentKind,
    text: readString(value.text, "", `${path}.text`, issues),
    fieldPath: contentKind === "Field"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    style: normalizeTextStyle(value.style, `${path}.style`, issues),
  };
}

function normalizeDetailTableColumn(
  value: unknown,
  path: string,
  columnIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableColumn | null {
  if (!isRecord(value)) {
    issues.push(createIssue("error", path, "明细列必须是对象。"));
    return null;
  }

  return {
    id: normalizeId(value.id, "detail-col", columnIds, `${path}.id`, issues),
    title: readString(value.title, "Column", `${path}.title`, issues),
    headerGroupTitle: readOptionalString(value.headerGroupTitle, `${path}.headerGroupTitle`, issues),
    headerGroupSpan: readOptionalNumber(value.headerGroupSpan, 1, 1, 20, `${path}.headerGroupSpan`, issues),
    contentKind: readOptionalEnum(value.contentKind, ["Field", "Composite"] as const, `${path}.contentKind`, issues) ?? "Field",
    fieldPath: readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
    content: normalizeDetailTableCellContentList(value.content, `${path}.content`, issues),
    widthMm: readNumber(value.widthMm, 30, 8, 180, `${path}.widthMm`, issues),
    align: readEnum(value.align, ["Left", "Center", "Right"] as const, "Left", `${path}.align`, issues),
    format: readOptionalString(value.format, `${path}.format`, issues),
    border: normalizeOptionalBorderStyle(value.border, `${path}.border`, issues),
  };
}

function normalizeDetailTableCellContentList(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableCellContent[] {
  if (value === undefined || value === null) {
    return [];
  }

  if (!Array.isArray(value)) {
    issues.push(createIssue("warning", path, "明细单元格组合内容必须是数组，已使用单字段列。"));
    return [];
  }

  const partIds = new Set<string>();
  return value
    .map((part, index) => normalizeDetailTableCellContent(part, `${path}[${index}]`, partIds, issues))
    .filter((part): part is ReportDetailTableCellContent => Boolean(part));
}

function normalizeDetailTableCellContent(
  value: unknown,
  path: string,
  partIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableCellContent | null {
  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "明细单元格组合片段无效，已忽略。"));
    return null;
  }

  const kind = readEnum(value.kind, ["Text", "Field", "LineBreak"] as const, "Text", `${path}.kind`, issues);
  return {
    id: normalizeId(value.id, "detail-cell-part", partIds, `${path}.id`, issues),
    kind,
    text: kind === "Text" ? readString(value.text, "", `${path}.text`, issues) : readOptionalString(value.text, `${path}.text`, issues) ?? "",
    fieldPath: kind === "Field"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
  };
}

function normalizeTextStyle(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]): ReportTextStyle {
  if (value === undefined || value === null) {
    return {};
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "文本样式无效，已使用默认样式。"));
    return {};
  }

  return {
    fontSizePt: readOptionalNumber(value.fontSizePt, 10, 6, 48, `${path}.fontSizePt`, issues),
    bold: typeof value.bold === "boolean" ? value.bold : undefined,
    align: readOptionalEnum(value.align, ["Left", "Center", "Right"] as const, `${path}.align`, issues),
    marginTopMm: readOptionalNumber(value.marginTopMm, 0, 0, 80, `${path}.marginTopMm`, issues),
    marginRightMm: readOptionalNumber(value.marginRightMm, 0, 0, 80, `${path}.marginRightMm`, issues),
    marginBottomMm: readOptionalNumber(value.marginBottomMm, 0, 0, 80, `${path}.marginBottomMm`, issues),
    marginLeftMm: readOptionalNumber(value.marginLeftMm, 0, 0, 80, `${path}.marginLeftMm`, issues),
  };
}

function normalizeBorderStyle(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]): ReportBorderStyle {
  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "边框样式无效，已使用默认边框。"));
    return {
      color: "#333333",
      widthPx: 1,
      top: true,
      right: true,
      bottom: true,
      left: true,
    };
  }

  const widthPx = readNumber(value.widthPx, 1, 0, 8, `${path}.widthPx`, issues);
  return {
    color: readCssColor(value.color, `${path}.color`, issues),
    widthPx,
    style: readOptionalEnum(value.style, ["Solid", "Dashed", "None"] as const, `${path}.style`, issues) ?? "Solid",
    top: readBorderSide(value.top, widthPx > 0, `${path}.top`, issues),
    right: readBorderSide(value.right, widthPx > 0, `${path}.right`, issues),
    bottom: readBorderSide(value.bottom, widthPx > 0, `${path}.bottom`, issues),
    left: readBorderSide(value.left, widthPx > 0, `${path}.left`, issues),
  };
}

function normalizeOptionalBorderStyle(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]): ReportBorderStyle | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "边框样式无效，已忽略。"));
    return undefined;
  }

  const widthPx = readNumber(value.widthPx, 0, 0, 8, `${path}.widthPx`, issues);
  return {
    color: readCssColor(value.color, `${path}.color`, issues),
    widthPx,
    style: readOptionalEnum(value.style, ["Solid", "Dashed", "None"] as const, `${path}.style`, issues) ?? "Solid",
    top: readBorderSide(value.top, false, `${path}.top`, issues),
    right: readBorderSide(value.right, false, `${path}.right`, issues),
    bottom: readBorderSide(value.bottom, false, `${path}.bottom`, issues),
    left: readBorderSide(value.left, false, `${path}.left`, issues),
  };
}

function readBorderSide(value: unknown, fallback: boolean, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined || value === null || value === "") {
    return fallback;
  }

  return readBoolean(value, fallback, path, issues);
}

function readBoolean(value: unknown, fallback: boolean, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value === "boolean") {
    return value;
  }

  if (value === undefined || value === null || value === "") {
    return fallback;
  }

  issues.push(createIssue("warning", path, "布尔值无效，已使用默认值。"));
  return fallback;
}

function readRequiredFieldPath(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  const fieldPath = typeof value === "string" ? value.trim() : "";
  if (isReportDesignerFieldPath(fieldPath)) {
    return fieldPath;
  }

  issues.push(createIssue("error", path, "字段名只能使用点分隔标识符，例如 Invoice.InvoiceNo。"));
  return "";
}

function readOptionalFieldPath(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined || value === null || value === "") {
    return "";
  }

  return readRequiredFieldPath(value, path, issues);
}

function readRequiredImageSource(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  const imageSource = typeof value === "string" ? value.trim() : "";
  if (isReportDesignerImageSource(imageSource)) {
    return imageSource;
  }

  issues.push(createIssue("error", path, "图片地址只允许 data:image、http(s) 或不含上级目录的相对路径。"));
  return "";
}

function readOptionalImageSource(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined || value === null || value === "") {
    return "";
  }

  return readRequiredImageSource(value, path, issues);
}

function readFontFamily(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value === "string" && value.trim() && isSafeReportDesignerCssFontFamily(value)) {
    return value.trim();
  }

  issues.push(createIssue("warning", path, "默认字体无效，已回退为跨平台开源字体栈。"));
  return portableReportSansFontFamily;
}

function readCssColor(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value === "string" && isReportDesignerCssColor(value)) {
    return value.trim();
  }

  issues.push(createIssue("warning", path, "颜色值无效，已回退为 #333333。"));
  return "#333333";
}

function readString(value: unknown, fallback: string, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value === "string") {
    return value;
  }

  issues.push(createIssue("warning", path, "文本值无效，已使用默认文本。"));
  return fallback;
}

function readOptionalString(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (typeof value === "string") {
    return value;
  }

  issues.push(createIssue("warning", path, "文本值无效，已忽略。"));
  return undefined;
}

function readNumber(
  value: unknown,
  fallback: number,
  min: number,
  max: number,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  const parsed = typeof value === "number" ? value : typeof value === "string" ? Number.parseFloat(value) : Number.NaN;
  if (!Number.isFinite(parsed)) {
    issues.push(createIssue("warning", path, "数字值无效，已使用默认值。"));
    return fallback;
  }

  const clamped = Math.min(max, Math.max(min, parsed));
  if (clamped !== parsed) {
    issues.push(createIssue("warning", path, `数字值超出范围，已限制在 ${min}-${max}。`));
  }

  return clamped;
}

function readOptionalNumber(
  value: unknown,
  fallback: number,
  min: number,
  max: number,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (value === undefined || value === null || value === "") {
    return undefined;
  }

  return readNumber(value, fallback, min, max, path, issues);
}

function readEnum<T extends string>(
  value: unknown,
  allowed: readonly T[],
  fallback: T,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): T {
  if (typeof value === "string" && (allowed as readonly string[]).includes(value)) {
    return value as T;
  }

  issues.push(createIssue("warning", path, "枚举值无效，已使用默认值。"));
  return fallback;
}

function readOptionalEnum<T extends string>(
  value: unknown,
  allowed: readonly T[],
  path: string,
  issues: ReportDesignerSchemaIssue[],
): T | undefined {
  if (value === undefined || value === null || value === "") {
    return undefined;
  }

  if (typeof value === "string" && (allowed as readonly string[]).includes(value)) {
    return value as T;
  }

  issues.push(createIssue("warning", path, "枚举值无效，已忽略。"));
  return undefined;
}

function normalizeId(
  value: unknown,
  fallbackPrefix: string,
  usedIds: Set<string>,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  const baseId = typeof value === "string" && value.trim()
    ? value.trim()
    : `${fallbackPrefix}-${usedIds.size + 1}`;
  let candidate = baseId;
  let suffix = 2;

  while (usedIds.has(candidate)) {
    candidate = `${baseId}-${suffix}`;
    suffix += 1;
  }

  if (candidate !== value) {
    issues.push(createIssue("warning", path, "ID 缺失或重复，已自动修正。"));
  }

  usedIds.add(candidate);
  return candidate;
}

function createIssue(
  severity: ReportDesignerSchemaIssue["severity"],
  path: string,
  message: string,
): ReportDesignerSchemaIssue {
  return {
    severity,
    path,
    message,
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
