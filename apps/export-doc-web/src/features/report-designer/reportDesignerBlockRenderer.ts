import type { ReportBlock, ReportBorderStyle, ReportTextStyle } from "./reportDesignerSchema.ts";
import { portableReportSansFontFamily } from "../../app/typographyPolicy.ts";
import { renderReportField as renderFieldExpression, shippingMarksFieldPath, shippingMarksPreviewText } from "./reportDesignerFieldRendering.ts";
import {
  isReportDesignerCssColor,
  isReportDesignerFieldPath,
  isReportDesignerImageSource,
} from "./reportDesignerSchemaValidation.ts";
import { renderGridDiagonalHeader } from "./reportDesignerGridDiagonal.ts";
import { detailSpanBorder, renderDetailFixedRow } from "./reportDesignerDetailFixedRows.ts";
import { renderDetailComposite } from "./reportDesignerDetailComposite.ts";
import { designerSampleValue } from "./reportDesignerCanvasSamples.ts";

/** Structured HTML for editor/reference fixtures; final document output runs in Rust. */
export function renderReportDesignerBlockToHtml(block: ReportBlock) {
  return renderBlock(block);
}

/**
 * Render synthetic examples directly from the AST for visual editing.
 * No business data is evaluated or persisted; final output runs in Rust.
 */
export function renderReportDesignerBlockPreviewToHtml(block: ReportBlock, heightMm?: number, widthMm?: number, mode: "products" | "entries" = "products", labels: ReadonlyMap<string, string> = new Map()) {
  if (block.type === "DetailTable") return renderDetailTable(block, heightMm, { widthMm, mode, labels });
  // Keep one representative field value, without also showing its fallback.
  let html = renderBlock(block, true, heightMm).replace(
    /\{\{ if ([A-Za-z_][A-Za-z0-9_.]*) \}\}(\{\{ \1 \}\})\{\{ else \}\}[\s\S]*?\{\{ end \}\}/g, "$2");
  html = html.replace(/\{\{\s*(?:if|else|end|capture|assign|while|case|when)[\s\S]*?\}\}/gi, "");
  html = html.replace(/\{\{\s*\$?[A-Za-z_][A-Za-z0-9_]*\s*=\s*[\s\S]*?\}\}/g, "");
  html = html.split(`{{ ${shippingMarksFieldPath} }}`).join(shippingMarksPreviewText);
  return html;
}

function renderBlock(block: ReportBlock, preview = false, heightMm?: number) {
  switch (block.type) {
    case "Text":
      return `<div style="${renderBoxStyle(block.style, block.border)}">${escapeHtml(block.text)}</div>`;
    case "Field":
      return `<div style="${renderBoxStyle(block.style, block.border)}">${block.label ? `${escapeHtml(block.label)}: ` : ""}<span>${renderFieldExpression(block.fieldPath, block.fallbackText)}</span></div>`;
    case "Row":
      return renderRowBlock(block);
    case "Grid":
      return renderGridBlock(block, preview);
    case "Conditional":
      return renderConditionalBlock(block, preview);
    case "Image":
      return renderImageBlock(block);
    case "DetailTable":
      return renderDetailTable(block, heightMm);
    case "PageBreak":
      return `<div class="report-page-break-row"></div>`;
  }
}

function renderGridBlock(block: Extract<ReportBlock, { type: "Grid" }>, preview: boolean) {
  const columnWidthTotal = block.columns.reduce((sum, column) => sum + Math.max(1, column.widthPercent), 0);
  const colgroup = `<colgroup>${block.columns.map((column) => {
    const width = Math.max(1, column.widthPercent) / columnWidthTotal * 100;
    return `<col style="width: ${width}%;">`;
  }).join("")}</colgroup>`;
  const rows = block.rows.map((row, rowIndex) => `<tr${row.heightMm ? ` style="height: ${row.heightMm}mm;"` : ""}>${row.cells.map((cell) => {
    const colSpan = Math.max(1, Math.floor(cell.colSpan ?? 1));
    const rowSpan = Math.max(1, Math.floor(cell.rowSpan ?? 1));
    const spanAttributes = `${colSpan > 1 ? ` colspan="${colSpan}"` : ""}${rowSpan > 1 ? ` rowspan="${rowSpan}"` : ""}`;
    const previewAttributes = preview
      ? ` data-report-grid-cell-id="${escapeHtml(cell.id)}"`
      : "";
    const spannedRows = block.rows.slice(rowIndex, rowIndex + rowSpan);
    const style = { ...block.defaultCellStyle, ...cell.style };
    const imageHeightMm = spannedRows.every((item) => item.heightMm)
      ? spannedRows.reduce((height, item) => height + item.heightMm!, 0) - (style.marginTopMm ?? 1.2) - (style.marginBottomMm ?? 1.2)
      : undefined;
    if (cell.diagonalHeader) {
      return `<td${spanAttributes}${previewAttributes} style="${renderGridCellStyle(block, cell)}">${renderGridDiagonalHeader(cell.diagonalHeader)}</td>`;
    }
    return `<td${spanAttributes}${previewAttributes} style="${renderGridCellStyle(block, cell)}">${renderGridCellContent(cell, imageHeightMm)}</td>`;
  }).join("")}</tr>`).join("");

  const caption = block.title ? `<caption class="edm-report-grid-title">${escapeHtml(block.title)}</caption>` : "";
  return `<table class="edm-report-grid" style="${renderGridBlockStyle(block)}">${caption}${colgroup}<tbody>${rows}</tbody></table>`;
}

function renderGridBlockStyle(block: Extract<ReportBlock, { type: "Grid" }>) {
  return [
    block.marginTopMm ? `margin-top: ${block.marginTopMm}mm` : "",
    block.marginBottomMm ? `margin-bottom: ${block.marginBottomMm}mm` : "",
  ].filter(Boolean).join("; ");
}

function renderGridCellStyle(
  block: Extract<ReportBlock, { type: "Grid" }>,
  cell: Extract<ReportBlock, { type: "Grid" }>["rows"][number]["cells"][number],
) {
  const style = { ...block.defaultCellStyle, ...cell.style };
  return [
    renderTextPresentation(style),
    cell.verticalText ? "writing-mode: vertical-rl" : "",
    cell.verticalText ? "text-orientation: upright" : "",
    cell.diagonalHeader ? "position: relative" : "",
    cell.diagonalHeader ? "padding: 0" : "",
    `vertical-align: ${(style.verticalAlign ?? "Middle").toLowerCase()}`,
    renderBorderStyle(cell.border ?? block.border),
    renderCellPadding(style, 1.2),
  ].filter(Boolean).join("; ");
}


function renderGridCellContent(cell: Extract<ReportBlock, { type: "Grid" }>["rows"][number]["cells"][number], imageHeightMm?: number) {
  switch (cell.contentKind) {
    case "Field":
      return `${cell.label ? (cell.labelPosition === "Above" ? `<span style="display:block">${escapeHtml(cell.label)}</span>` : `${escapeHtml(cell.label)}${cell.labelPosition === "Prefix" ? "" : ": "}`) : ""}${renderFieldExpression(cell.fieldPath, cell.fallbackText, imageHeightMm)}`;
    case "CheckboxGroup":
      return renderGridCheckboxGroup(cell);
    case "Text":
      return escapeHtml(cell.text);
  }
}

function renderGridCheckboxGroup(cell: Extract<ReportBlock, { type: "Grid" }>["rows"][number]["cells"][number]) {
  const fieldPath = cell.fieldPath.trim();
  const options = cell.checkboxOptions ?? [];
  if (!isReportDesignerFieldPath(fieldPath) || options.length === 0) {
    return `<span class="edm-report-grid-checkboxes">${options.map((option) => `<span class="edm-report-grid-checkbox">☐ ${escapeHtml(option.label)}</span>`).join("")}</span>`;
  }

  return `<span class="edm-report-grid-checkboxes">${options.map((option) => {
    const checked = `{{ if ${fieldPath} == ${renderScribanStringLiteral(option.value)} }}☑{{ else }}☐{{ end }}`;
    return `<span class="edm-report-grid-checkbox">${checked} ${escapeHtml(option.label)}</span>`;
  }).join("")}</span>`;
}

function renderRowBlock(block: Extract<ReportBlock, { type: "Row" }>) {
  return `<table class="edm-report-row" style="${renderRowStyle(block)}"><tbody><tr>${block.columns.map((column) => {
    const content = column.contentKind === "Field"
      ? `${column.label ? `${escapeHtml(column.label)}: ` : ""}<span>${renderFieldExpression(column.fieldPath, column.fallbackText)}</span>`
      : escapeHtml(column.text);
    return `<td style="${renderRowCellStyle(column)}">${content}</td>`;
  }).join("")}</tr></tbody></table>`;
}

function renderRowStyle(block: Extract<ReportBlock, { type: "Row" }>) {
  return [
    block.marginTopMm ? `margin-top: ${block.marginTopMm}mm` : "",
    block.marginBottomMm ? `margin-bottom: ${block.marginBottomMm}mm` : "",
  ].filter(Boolean).join("; ");
}

function renderRowCellStyle(column: Extract<ReportBlock, { type: "Row" }>["columns"][number]) {
  return [
    `width: ${Math.max(1, column.widthPercent)}%`,
    renderBoxStyle(column.style, column.border),
  ].filter(Boolean).join("; ");
}

function renderImageBlock(block: Extract<ReportBlock, { type: "Image" }>) {
  const src = renderImageSource(block);
  if (!src) {
    return "";
  }

  const imageHtml = `<div class="edm-image-block${block.keepTogether ? " edm-image-keep-together" : ""}" style="${renderImageWrapperStyle(block)}"><img src="${src}" alt="${escapeHtmlAttribute(block.altText ?? block.title ?? "")}" style="${renderImageStyle(block)}"></div>`;
  if (block.hideWhenSourceEmpty && block.sourceKind === "Field" && isReportDesignerFieldPath(block.fieldPath)) {
    return `{{ if ${block.fieldPath.trim()} }}${imageHtml}{{ end }}`;
  }

  return imageHtml;
}

function renderImageSource(block: Extract<ReportBlock, { type: "Image" }>) {
  if (block.sourceKind === "Field") {
    return isReportDesignerFieldPath(block.fieldPath) ? `{{ ${block.fieldPath.trim()} }}` : "";
  }

  const url = block.url.trim();
  return isReportDesignerImageSource(url) ? escapeHtmlAttribute(url) : "";
}

function renderImageWrapperStyle(block: Extract<ReportBlock, { type: "Image" }>) {
  return [
    `text-align: ${alignToCss(block.align)}`,
    block.marginTopMm ? `margin-top: ${block.marginTopMm}mm` : "",
    block.marginBottomMm ? `margin-bottom: ${block.marginBottomMm}mm` : "",
  ].filter(Boolean).join("; ");
}

function renderImageStyle(block: Extract<ReportBlock, { type: "Image" }>) {
  return [
    `width: ${block.widthMm}mm`,
    block.heightMm ? `height: ${block.heightMm}mm` : "height: auto",
  ].filter(Boolean).join("; ");
}

function renderConditionalBlock(block: Extract<ReportBlock, { type: "Conditional" }>, preview: boolean) {
  const content = block.content.kind === "Field"
    ? `${block.content.label ? `${escapeHtml(block.content.label)}: ` : ""}<span>${renderFieldExpression(block.content.fieldPath, block.content.fallbackText)}</span>`
    : escapeHtml(block.content.text);

  const body = `<div class="edm-conditional-block" style="${renderBoxStyle(block.style, block.border)}">${content}</div>`;
  // The canvas always shows editable content. Actual conditions run in Rust;
  // the legacy HTML fixture supports only its original reference scenarios.
  if (preview) return body;
  const condition = renderConditionalExpression(block);
  return condition ? `{{ if ${condition} }}${body}{{ end }}` : "";
}

function renderConditionalExpression(block: Extract<ReportBlock, { type: "Conditional" }>) {
  if (block.additionalConditions?.length || (block.condition.comparisonType ?? "Text") !== "Text" || block.condition.ignoreCase) {
    throw new Error("组合或类型条件请使用原生预览，旧 HTML 夹具不执行这些规则。");
  }
  const fieldPath = block.condition.fieldPath.trim();
  if (!isReportDesignerFieldPath(fieldPath)) {
    return "";
  }

  switch (block.condition.operator) {
    case "Equals":
      return `${fieldPath} == ${renderScribanStringLiteral(block.condition.value)}`;
    case "NotEquals":
      return `${fieldPath} != ${renderScribanStringLiteral(block.condition.value)}`;
    case "HasValue":
      return fieldPath;
    default:
      throw new Error("扩展条件请使用原生预览，旧 HTML 夹具不执行这些规则。");
  }
}

function renderDetailTable(block: Extract<ReportBlock, { type: "DetailTable" }>, heightMm?: number, preview?: { widthMm?: number; mode: "products" | "entries"; labels: ReadonlyMap<string, string> }) {
  const fillHeight = block.print.fillHeight && heightMm !== undefined && Number.isFinite(heightMm) && heightMm > 0;
  const fillRow = fillHeight ? `<tr class="edm-detail-fill-row" style="height:100%">${block.columns.map(column => `<td style="${renderDetailCellStyle({}, { ...(column.border ?? block.border), top: false, bottom: false }, column.align)}"></td>`).join("")}</tr>` : "";
  const sample = (path: string, index = 0) => escapeHtml(path === shippingMarksFieldPath ? shippingMarksPreviewText : designerSampleValue(path, index, preview?.labels ?? new Map()));
  const renderers = { style: renderDetailCellStyle, field: preview ? sample : renderFieldExpression, text: escapeHtml };
  const summaryRow = renderDetailFixedRow(block, block.summaryRow, renderers);
  const adjustmentRow = block.adjustments ? `<tr>${block.columns.map(column => `<td style="${renderDetailCellStyle(block.bodyStyle, { ...(column.border ?? block.border), top: false, bottom: false }, column.align)}">${column.id === block.adjustments!.descriptionColumnId ? "金额调整说明（有数据时显示）" : column.id === block.adjustments!.amountColumnId ? "增减金额" : ""}</td>`).join("")}</tr>` : "";
  const detailPrintClasses = renderDetailPrintClassNames(block);
  const makeRow = (index: number, entry?: "note" | "blank" | "item") => `<tr${entry ? ` data-report-entry-kind="${entry}"` : ""}>${block.columns
    .map((column) => {
      const border = column.border ?? block.border;
      const content = entry && column.id === block.entryDescriptionColumnId
        ? entry === "blank" ? "<br>" : entry === "note" ? "按录入顺序打印的说明文字" : sample("item.StyleName", index)
        : entry && entry !== "item" ? "" : renderDetailCellContent(column, block.bodyStyle.verticalAlign, preview ? path => sample(path, index) : undefined);
      return `<td${preview ? ` data-report-detail-column-id="${escapeHtmlAttribute(column.id)}"` : ""} style="${renderDetailCellStyle(block.bodyStyle, block.rowSeparators === false ? { ...border, top: false, bottom: false } : border, column.align)}">${content}</td>`;
    })
    .join("")}</tr>`;
  const entryMode = preview?.mode === "entries" && block.entryDescriptionColumnId;
  const rows = preview ? entryMode ? makeRow(0, "note") + makeRow(0, "blank") + makeRow(0, "item") + makeRow(1, "item") : makeRow(0) + makeRow(1) : renderDetailTableRows(block, makeRow(0));
  const introRow = renderDetailFixedRow(block, block.introRow, renderers);
  const columns = renderProportionalColumns(block.columns.map(column => column.widthMm));
  const header = `<thead>${renderDetailTableHeaderRows(block)}</thead>`;
  const table = `<table class="edm-detail-table ${detailPrintClasses}" style="${renderBorderStyle(block.border)}${fillHeight ? `;height:${block.sideBand ? "100%" : `${heightMm}mm`}` : ""}">${columns}${block.sideBand ? "" : header}<tbody>${introRow}${rows}${adjustmentRow}${summaryRow}${fillRow}</tbody></table>`;

  if (!block.sideBand) {
    return table;
  }

  const sideContent = block.sideBand.contentKind === "Field"
    ? preview ? sample(block.sideBand.fieldPath) : renderFieldExpression(block.sideBand.fieldPath)
    : escapeHtml(block.sideBand.text);

  return `<table class="edm-detail-layout ${renderDetailHeaderRepeatClassName(block)}"${fillHeight ? ` style="height:${heightMm}mm"` : ""}>
  ${renderProportionalColumns([block.sideBand.widthMm, preview?.widthMm ? Math.max(1, preview.widthMm - block.sideBand.widthMm) : block.columns.reduce((sum, column) => sum + column.widthMm, 0)])}
  <thead>
    <tr>
      <th style="${renderDetailLayoutCellStyle(block.headerStyle, block.border)}">${escapeHtml(block.sideBand.title)}</th>
      <th style="padding:0"><table class="edm-detail-table">${columns}${header}</table></th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td style="${renderDetailLayoutCellStyle(block.sideBand.style, block.border, "top")}">${sideContent}</td>
      <td style="${renderDetailLayoutCellStyle({}, block.border, "top")}; padding: 0;">${table}</td>
    </tr>
  </tbody>
</table>`;
}

function renderDetailTableRows(
  block: Extract<ReportBlock, { type: "DetailTable" }>,
  detailRow: string,
) {
  if (!block.grouping?.footer) {
    if (block.grouping?.pageBreakBefore) {
      return renderDetailTableRowsWithGroupPageBreaks(block, detailRow);
    }

    const groupPrefix = renderDetailTableGroupPrefix(block);
    const groupRow = renderDetailTableGroupRow(block);
    return `${groupPrefix}{{ for item in Invoice.Items }}${groupRow}${detailRow}{{ end }}`;
  }

  const groupField = renderDetailItemValueReference(block.grouping.fieldPath);
  if (!groupField) {
    return `{{ for item in Invoice.Items }}${detailRow}{{ end }}`;
  }

  const groupVariable = renderScribanVariableName(block.id, "group");
  const currentVariable = renderScribanVariableName(block.id, "current_group");
  const startedVariable = renderScribanVariableName(block.id, "group_started");
  const prefix = renderDetailTableGroupFooterPrefix(block);
  const boundary = renderDetailTableGroupBoundary(block, groupField, groupVariable, currentVariable, startedVariable);
  const accumulators = renderDetailTableGroupFooterAccumulators(block);
  const finalFooter = `{{ if ${startedVariable} }}${renderDetailTableGroupFooterRow(block)}{{ end }}`;

  return `${prefix}{{ for item in Invoice.Items }}${boundary}${detailRow}${accumulators}{{ end }}${finalFooter}`;
}

function renderDetailTableRowsWithGroupPageBreaks(
  block: Extract<ReportBlock, { type: "DetailTable" }>,
  detailRow: string,
) {
  const groupField = block.grouping ? renderDetailItemValueReference(block.grouping.fieldPath) : "";
  if (!block.grouping || !groupField) {
    return `{{ for item in Invoice.Items }}${detailRow}{{ end }}`;
  }

  const groupVariable = renderScribanVariableName(block.id, "group");
  const currentVariable = renderScribanVariableName(block.id, "current_group");
  const startedVariable = renderScribanVariableName(block.id, "group_started");
  const prefix = `{{ ${groupVariable} = "" }}{{ ${startedVariable} = false }}`;
  const firstGroupOpen = renderDetailTableGroupOpen(block, groupVariable, currentVariable, startedVariable, false);
  const nextGroupOpen = renderDetailTableGroupOpen(block, groupVariable, currentVariable, startedVariable, true);
  const boundary = `{{ ${currentVariable} = ${groupField} }}{{ if ${startedVariable} == false }}${firstGroupOpen}{{ else }}{{ if ${currentVariable} != ${groupVariable} }}${nextGroupOpen}{{ end }}{{ end }}`;

  return `${prefix}{{ for item in Invoice.Items }}${boundary}${detailRow}{{ end }}`;
}

function renderDetailTableHeaderRows(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  const groupRow = renderDetailTableHeaderGroupRow(block);
  const columnRow = `<tr>${block.columns
    .map((column) => `<th data-report-detail-column-id="${escapeHtmlAttribute(column.id)}" style="${renderDetailCellStyle(block.headerStyle, column.border ?? block.border, column.align)}">${escapeHtml(column.title)}</th>`)
    .join("")}</tr>`;

  return `${groupRow}${columnRow}`;
}

function renderDetailTableHeaderGroupRow(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  if (!block.columns.some((column) => column.headerGroupTitle)) {
    return "";
  }

  const cells: string[] = [];
  let index = 0;
  while (index < block.columns.length) {
    const column = block.columns[index];
    const requestedSpan = column.headerGroupTitle ? Math.max(1, Math.floor(column.headerGroupSpan ?? 1)) : 1;
    const columnSpan = Math.min(requestedSpan, block.columns.length - index);
    const title = column.headerGroupTitle ?? "";
    cells.push(`<th colspan="${columnSpan}" style="${renderDetailCellStyle(block.headerStyle, detailSpanBorder(block, index, columnSpan), column.align)}">${escapeHtml(title)}</th>`);
    index += columnSpan;
  }

  return `<tr class="edm-detail-header-group-row">${cells.join("")}</tr>`;
}

function renderDetailTableGroupPrefix(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  if (!block.grouping || !renderDetailItemValueReference(block.grouping.fieldPath)) {
    return "";
  }

  const groupVariable = renderScribanVariableName(block.id, "group");
  return `{{ ${groupVariable} = "" }}`;
}

function renderDetailTableGroupRow(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  if (!block.grouping) {
    return "";
  }

  const groupField = renderDetailItemValueReference(block.grouping.fieldPath);
  if (!groupField) {
    return "";
  }

  const groupVariable = renderScribanVariableName(block.id, "group");
  const currentVariable = renderScribanVariableName(block.id, "current_group");
  const className = [
    "edm-detail-group-row",
    block.grouping.keepTogether ? "edm-detail-group-keep" : "",
  ].filter(Boolean).join(" ");
  const label = escapeHtml(block.grouping.label);
  const value = block.grouping.showFieldValue ? ` <span>{{ ${currentVariable} }}</span>` : "";

  return `{{ ${currentVariable} = ${groupField} }}{{ if ${currentVariable} != ${groupVariable} }}<tr class="${className}"><td colspan="${Math.max(1, block.columns.length)}" style="${renderDetailCellStyle(block.grouping.style, block.border, block.grouping.style.align ?? "Left")}">${label}${value}</td></tr>{{ ${groupVariable} = ${currentVariable} }}{{ end }}`;
}

function renderDetailTableGroupFooterPrefix(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  const groupVariable = renderScribanVariableName(block.id, "group");
  const startedVariable = renderScribanVariableName(block.id, "group_started");
  const countVariable = renderScribanVariableName(block.id, "group_count");
  const sumInitializers = collectGroupFooterSumCells(block)
    .map((cell) => `{{ ${renderGroupFooterSumVariable(block.id, cell.columnId)} = 0 }}`)
    .join("");

  return `{{ ${groupVariable} = "" }}{{ ${startedVariable} = false }}{{ ${countVariable} = 0 }}${sumInitializers}`;
}

function renderDetailTableGroupBoundary(
  block: Extract<ReportBlock, { type: "DetailTable" }>,
  groupField: string,
  groupVariable: string,
  currentVariable: string,
  startedVariable: string,
) {
  const firstGroupOpen = renderDetailTableGroupOpen(block, groupVariable, currentVariable, startedVariable, false);
  const nextGroupOpen = renderDetailTableGroupOpen(block, groupVariable, currentVariable, startedVariable, Boolean(block.grouping?.pageBreakBefore));
  const groupFooter = renderDetailTableGroupFooterRow(block);
  const reset = renderDetailTableGroupFooterReset(block);

  return `{{ ${currentVariable} = ${groupField} }}{{ if ${startedVariable} == false }}${firstGroupOpen}{{ else }}{{ if ${currentVariable} != ${groupVariable} }}${groupFooter}${reset}${nextGroupOpen}{{ end }}{{ end }}`;
}

function renderDetailTableGroupOpen(
  block: Extract<ReportBlock, { type: "DetailTable" }>,
  groupVariable: string,
  currentVariable: string,
  startedVariable: string,
  pageBreakBefore = false,
) {
  const headerRow = renderDetailTableGroupHeaderRow(block, currentVariable, pageBreakBefore);
  return `${headerRow}{{ ${groupVariable} = ${currentVariable} }}{{ ${startedVariable} = true }}`;
}

function renderDetailTableGroupHeaderRow(
  block: Extract<ReportBlock, { type: "DetailTable" }>,
  currentVariable: string,
  pageBreakBefore = false,
) {
  if (!block.grouping) {
    return "";
  }

  const className = [
    "edm-detail-group-row",
    block.grouping.keepTogether ? "edm-detail-group-keep" : "",
    pageBreakBefore ? "edm-detail-group-page-break" : "",
  ].filter(Boolean).join(" ");
  const label = escapeHtml(block.grouping.label);
  const value = block.grouping.showFieldValue ? ` <span>{{ ${currentVariable} }}</span>` : "";

  return `<tr class="${className}"><td colspan="${Math.max(1, block.columns.length)}" style="${renderDetailCellStyle(block.grouping.style, block.border, block.grouping.style.align ?? "Left")}">${label}${value}</td></tr>`;
}

function renderDetailTableGroupFooterReset(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  const countVariable = renderScribanVariableName(block.id, "group_count");
  const sumResets = collectGroupFooterSumCells(block)
    .map((cell) => `{{ ${renderGroupFooterSumVariable(block.id, cell.columnId)} = 0 }}`)
    .join("");

  return `{{ ${countVariable} = 0 }}${sumResets}`;
}

function renderDetailTableGroupFooterAccumulators(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  const countVariable = renderScribanVariableName(block.id, "group_count");
  const sumAccumulators = collectGroupFooterSumCells(block)
    .map((cell) => {
      const fieldReference = renderDetailItemValueReference(cell.fieldPath);
      if (!fieldReference) {
        return "";
      }

      const sumVariable = renderGroupFooterSumVariable(block.id, cell.columnId);
      return `{{ ${sumVariable} = ${sumVariable} + ${fieldReference} }}`;
    })
    .filter(Boolean)
    .join("");

  return `{{ ${countVariable} = ${countVariable} + 1 }}${sumAccumulators}`;
}

function renderDetailTableGroupFooterRow(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  if (!block.grouping?.footer) {
    return "";
  }

  const footer = block.grouping.footer;
  const labelColumnSpan = Math.min(block.columns.length, Math.max(1, Math.floor(footer.labelColumnSpan)));
  const cellsByColumnId = new Map(footer.cells.map((cell) => [cell.columnId, cell]));
  const valueCells = block.columns.slice(labelColumnSpan).map((column) => {
    const cell = cellsByColumnId.get(column.id);
    const content = cell ? renderGroupFooterCellContent(block, cell) : "";

    return `<td style="${renderDetailCellStyle(footer.style, column.border ?? block.border, footer.style.align ?? column.align)}">${content}</td>`;
  }).join("");

  const label = [escapeHtml(footer.label), ...block.columns.slice(0, labelColumnSpan).map(column => {
    const cell = cellsByColumnId.get(column.id); return cell ? renderGroupFooterCellContent(block, cell) : "";
  })].filter(Boolean).join("<br>");
  return `<tr class="edm-detail-group-footer-row"><td colspan="${labelColumnSpan}" style="${renderDetailCellStyle(footer.style, detailSpanBorder(block, 0, labelColumnSpan), footer.style.align ?? "Right")}">${label}</td>${valueCells}</tr>`;
}

function renderGroupFooterCellContent(
  block: Extract<ReportBlock, { type: "DetailTable" }>,
  cell: NonNullable<NonNullable<Extract<ReportBlock, { type: "DetailTable" }>["grouping"]>["footer"]>["cells"][number],
) {
  switch (cell.contentKind) {
    case "Sum":
      return renderDetailItemValueReference(cell.fieldPath) ? `{{ ${renderGroupFooterSumVariable(block.id, cell.columnId)} }}` : "";
    case "Count":
      return `{{ ${renderScribanVariableName(block.id, "group_count")} }}`;
    case "Text":
      return escapeHtml(cell.text);
    case "Empty":
      return "";
  }
}

function collectGroupFooterSumCells(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  return block.grouping?.footer?.cells.filter((cell) => cell.contentKind === "Sum" && renderDetailItemValueReference(cell.fieldPath)) ?? [];
}

function renderDetailPrintClassNames(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  return [
    renderDetailHeaderRepeatClassName(block),
    block.print.keepRowsTogether ? "edm-detail-keep-rows" : "edm-detail-split-rows",
    block.print.firstPageRows ? `edm-detail-first-rows-${block.print.firstPageRows}` : "",
    block.print.continuationPageRows ? `edm-detail-continuation-rows-${block.print.continuationPageRows}` : "",
  ].join(" ");
}

function renderDetailHeaderRepeatClassName(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  return block.print.repeatHeaderOnPageBreak ? "edm-detail-repeat-header" : "edm-detail-no-repeat-header";
}

function renderDetailCellContent(column: Extract<ReportBlock, { type: "DetailTable" }>["columns"][number], verticalAlign?: ReportTextStyle["verticalAlign"], field = renderDetailItemExpression) {
  if (column.contentKind !== "Composite" || !column.content || column.content.length === 0) {
    return field(column.fieldPath);
  }
  if (column.omitEmptyLines || column.content.some(part => part.kind === "ColumnBreak" || part.kind === "Spacer" || part.condition)) {
    return `<div class="${column.omitEmptyLines ? "edm-detail-omit-empty-lines" : ""}">${renderDetailComposite(column.content, part => part.kind === "Text" ? escapeHtml(part.text) : field(part.fieldPath), verticalAlign)}</div>`;
  }

  return column.content.filter(part => part.visible !== false).map((part) => {
    switch (part.kind) {
      case "Text":
        return escapeHtml(part.text);
      case "Field":
        return field(part.fieldPath);
      case "LineBreak":
        return "<br>";
    }
  }).join("");
}

function renderProportionalColumns(widths: number[]) {
  const total = widths.reduce((sum, width) => sum + Math.max(1, width), 0);
  return `<colgroup>${widths.map(width => `<col style="width:${Math.max(1, width) / total * 100}%">`).join("")}</colgroup>`;
}

function renderDetailLayoutCellStyle(
  style: ReportTextStyle,
  border: ReportBorderStyle,
  verticalAlign?: "top" | "middle" | "bottom",
) {
  return [
    renderTextStyle(style),
    verticalAlign ? `vertical-align: ${verticalAlign}` : "",
    "white-space: pre-wrap",
    "overflow-wrap: anywhere",
    "word-break: break-word",
    renderBorderStyle(border),
  ].filter(Boolean).join("; ");
}

function renderDetailCellStyle(
  style: ReportTextStyle,
  border: ReportBorderStyle,
  align: "Left" | "Center" | "Right",
) {
  return [
    `text-align: ${alignToCss(align)}`,
    `vertical-align: ${(style.verticalAlign ?? "Top").toLowerCase()}`,
    style.fontSizePt ? `font-size: ${style.fontSizePt}pt` : "",
    style.bold ? `font-weight: 700; font-family: ${portableReportSansFontFamily}` : "",
    style.marginTopMm ? `padding-top: ${style.marginTopMm}mm` : "",
    style.marginRightMm ? `padding-right: ${style.marginRightMm}mm` : "",
    style.marginBottomMm ? `padding-bottom: ${style.marginBottomMm}mm` : "",
    style.marginLeftMm ? `padding-left: ${style.marginLeftMm}mm` : "",
    "overflow-wrap: anywhere",
    "word-break: break-word",
    renderBorderStyle(border),
    renderCellPadding(style, 2),
  ].filter(Boolean).join("; ");
}

function renderDetailItemExpression(fieldPath: string) {
  const itemReference = renderDetailItemValueReference(fieldPath);
  return itemReference ? `{{ ${itemReference} }}` : "";
}

function renderDetailItemValueReference(fieldPath: string) {
  return isReportDesignerFieldPath(fieldPath.trim()) ? fieldPath.trim() : "";
}

function renderTextStyle(style: ReportTextStyle) {
  return [
    renderTextPresentation(style),
    style.marginTopMm ? `margin-top: ${style.marginTopMm}mm` : "",
    style.marginRightMm ? `margin-right: ${style.marginRightMm}mm` : "",
    style.marginBottomMm ? `margin-bottom: ${style.marginBottomMm}mm` : "",
    style.marginLeftMm ? `margin-left: ${style.marginLeftMm}mm` : "",
  ].filter(Boolean).join("; ");
}

function renderTextPresentation(style: ReportTextStyle) {
  return [
    style.fontSizePt ? `font-size: ${style.fontSizePt}pt` : "",
    style.bold ? `font-weight: 700; font-family: ${portableReportSansFontFamily}` : "",
    style.align ? `text-align: ${alignToCss(style.align)}` : "",
    style.verticalAlign ? `vertical-align: ${style.verticalAlign.toLowerCase()}` : "",
  ].filter(Boolean).join("; ");
}

function renderCellPadding(style: ReportTextStyle, fallbackMm: number) {
  return [
    `padding-top: ${style.marginTopMm ?? fallbackMm}mm`,
    `padding-right: ${style.marginRightMm ?? fallbackMm}mm`,
    `padding-bottom: ${style.marginBottomMm ?? fallbackMm}mm`,
    `padding-left: ${style.marginLeftMm ?? fallbackMm}mm`,
  ].join("; ");
}

function renderBoxStyle(style: ReportTextStyle, border?: ReportBorderStyle) {
  return [
    renderTextStyle(style),
    renderBorderStyle(border),
  ].filter(Boolean).join("; ");
}

function renderBorderStyle(border?: ReportBorderStyle) {
  if (!border) {
    return "";
  }

  if (border.widthPx <= 0 || border.style === "None") {
    return [
      "border-top: 0",
      "border-right: 0",
      "border-bottom: 0",
      "border-left: 0",
    ].join("; ");
  }

  const color = renderCssColor(border.color);
  const width = Math.max(0, border.widthPx);
  const lineStyle = border.style === "Dashed" ? "dashed" : "solid";
  const line = `${width}px ${lineStyle} ${color}`;
  return [
    `border-top: ${border.top ? line : "0"}`,
    `border-right: ${border.right ? line : "0"}`,
    `border-bottom: ${border.bottom ? line : "0"}`,
    `border-left: ${border.left ? line : "0"}`,
    border.top || border.right || border.bottom || border.left ? "padding: 2mm" : "",
  ].filter(Boolean).join("; ");
}

function alignToCss(value: "Left" | "Center" | "Right") {
  return value.toLowerCase();
}

function escapeHtml(value: string) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function escapeHtmlAttribute(value: string) {
  return escapeHtml(value).replace(/'/g, "&#39;");
}

function renderCssColor(value: string) {
  return isReportDesignerCssColor(value) ? value : "#333333";
}

function renderScribanStringLiteral(value: string) {
  return `"${value
    .replace(/\\/g, "\\\\")
    .replace(/"/g, "\\\"")
    .replace(/\r/g, "\\r")
    .replace(/\n/g, "\\n")}"`;
}

function renderScribanVariableName(blockId: string, suffix: string) {
  return `edm_${suffix}_${renderScribanSafeIdentifier(blockId)}`;
}

function renderGroupFooterSumVariable(blockId: string, columnId: string) {
  return `edm_group_sum_${renderScribanSafeIdentifier(blockId)}_${renderScribanSafeIdentifier(columnId)}`;
}

function renderScribanSafeIdentifier(value: string) {
  return value.replace(/[^A-Za-z0-9_]/g, "_").replace(/^([^A-Za-z_])/, "_$1");
}
