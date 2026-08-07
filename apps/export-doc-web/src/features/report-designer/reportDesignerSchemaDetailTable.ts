import type {
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
} from "./reportDesignerSchema.ts";
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
  readNumber,
  readOptionalEnum,
  readOptionalFieldPath,
  readOptionalNumber,
  readOptionalString,
  readRequiredFieldPath,
  readString,
  type ReportDesignerSchemaIssue,
} from "./reportDesignerSchemaValues.ts";

export function normalizeDetailTableBlock(
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
