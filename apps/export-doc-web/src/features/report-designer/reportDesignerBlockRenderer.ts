import type { ReportBlock, ReportBorderStyle, ReportTextStyle } from "./reportDesignerSchema.ts";
import {
  isReportDesignerCssColor,
  isReportDesignerFieldPath,
  isReportDesignerImageSource,
} from "./reportDesignerSchemaValidation.ts";

/** Shared structured-block renderer used by the v3 exporter for flow elements. */
export function renderReportDesignerBlockToHtml(block: ReportBlock) {
  return renderBlock(block);
}

/**
 * Render a deterministic, data-free preview for the V3 canvas.  Scriban
 * control statements are meaningful to the print renderer but would otherwise
 * leave a Flow element looking like an empty blue rectangle in the editor.
 * The block AST remains the source of truth; this only expands one
 * representative set of rows for visual editing and never gets persisted.
 */
export function renderReportDesignerBlockPreviewToHtml(block: ReportBlock) {
  // The editor preview shows one representative content value.  Do not carry
  // a conditional field's export fallback into this data-free rendering: the
  // control tags are intentionally removed below, so doing so would display
  // both the field token and the fallback at once.
  const previewBlock = block.type === "Conditional" && block.content.kind === "Field"
    ? { ...block, content: { ...block.content, fallbackText: undefined } }
    : block;
  let html = renderBlock(previewBlock);
  html = expandPreviewLoops(html);
  html = html.replace(/\{\{\s*(?:if|else|end|capture|assign|while|case|when)[\s\S]*?\}\}/gi, "");
  html = html.replace(/\{\{\s*\$?[A-Za-z_][A-Za-z0-9_]*\s*=\s*[\s\S]*?\}\}/g, "");
  return html;
}

function expandPreviewLoops(source: string) {
  const loopPattern = /\{\{\s*for\s+[^}]+\}\}([\s\S]*?)\{\{\s*end\s*\}\}/gi;
  let current = source;
  for (let pass = 0; pass < 4; pass += 1) {
    const next = current.replace(loopPattern, (_match, body: string) =>
      ["样例 1", "样例 2", "样例 3"].map((label) => body.replace(/\{\{\s*item\.([A-Za-z_][A-Za-z0-9_]*)\s*\}\}/gi, (_field, name: string) =>
        `<span class="edm-report-preview-sample-value">${escapeHtml(name === "Quantity" ? "2" : name === "TotalPrice" ? "1,234.00" : `${label} · ${name}`)}</span>`)).join(""),
    );
    if (next === current) return current;
    current = next;
  }
  return current;
}

function renderBlock(block: ReportBlock) {
  switch (block.type) {
    case "Text":
      return `<div style="${renderBoxStyle(block.style, block.border)}">${escapeHtml(block.text)}</div>`;
    case "Field":
      return `<div style="${renderBoxStyle(block.style, block.border)}">${block.label ? `${escapeHtml(block.label)}: ` : ""}<span>${renderFieldExpression(block.fieldPath)}</span></div>`;
    case "Row":
      return renderRowBlock(block);
    case "Grid":
      return renderGridBlock(block);
    case "Conditional":
      return renderConditionalBlock(block);
    case "Image":
      return renderImageBlock(block);
    case "DetailTable":
      return renderDetailTable(block);
    case "PageBreak":
      return `<div class="report-page-break-row"></div>`;
  }
}

function renderGridBlock(block: Extract<ReportBlock, { type: "Grid" }>) {
  const columnWidthTotal = block.columns.reduce((sum, column) => sum + Math.max(1, column.widthPercent), 0);
  const colgroup = `<colgroup>${block.columns.map((column) => {
    const width = Math.round((Math.max(1, column.widthPercent) / columnWidthTotal) * 10000) / 100;
    return `<col style="width: ${width}%;">`;
  }).join("")}</colgroup>`;
  const rows = block.rows.map((row) => `<tr${row.heightMm ? ` style="height: ${row.heightMm}mm;"` : ""}>${row.cells.map((cell) => {
    const colSpan = Math.max(1, Math.floor(cell.colSpan ?? 1));
    const rowSpan = Math.max(1, Math.floor(cell.rowSpan ?? 1));
    const spanAttributes = `${colSpan > 1 ? ` colspan="${colSpan}"` : ""}${rowSpan > 1 ? ` rowspan="${rowSpan}"` : ""}`;
    return `<td${spanAttributes} style="${renderGridCellStyle(block, cell)}">${renderGridCellContent(cell)}</td>`;
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
  return [
    renderTextStyle({ ...block.defaultCellStyle, ...cell.style }),
    cell.verticalText ? "writing-mode: vertical-rl" : "",
    cell.verticalText ? "text-orientation: upright" : "",
    "vertical-align: middle",
    "padding: 1.2mm",
    renderBorderStyle(cell.border ?? block.border),
  ].filter(Boolean).join("; ");
}

function renderGridCellContent(cell: Extract<ReportBlock, { type: "Grid" }>["rows"][number]["cells"][number]) {
  switch (cell.contentKind) {
    case "Field":
      return `${cell.label ? `${escapeHtml(cell.label)}: ` : ""}${renderFieldExpression(cell.fieldPath)}`;
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
      ? `${column.label ? `${escapeHtml(column.label)}: ` : ""}<span>${renderFieldExpression(column.fieldPath)}</span>`
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

function renderConditionalBlock(block: Extract<ReportBlock, { type: "Conditional" }>) {
  const condition = renderConditionalExpression(block);
  if (!condition) {
    return "";
  }

  const content = block.content.kind === "Field"
    ? `${block.content.label ? `${escapeHtml(block.content.label)}: ` : ""}<span>${renderFieldExpression(block.content.fieldPath, block.content.fallbackText)}</span>`
    : escapeHtml(block.content.text);

  return `{{ if ${condition} }}<div class="edm-conditional-block" style="${renderBoxStyle(block.style, block.border)}">${content}</div>{{ end }}`;
}

function renderConditionalExpression(block: Extract<ReportBlock, { type: "Conditional" }>) {
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
  }
}

function renderDetailTable(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  const summaryRow = renderDetailTableSummaryRow(block);
  const detailPrintClasses = renderDetailPrintClassNames(block);
  const detailRow = `<tr>${block.columns
    .map((column) => `<td style="${renderDetailCellStyle(block.bodyStyle, column.border ?? block.border, column.align)}">${renderDetailCellContent(column)}</td>`)
    .join("")}</tr>`;
  const table = `<table class="edm-detail-table ${detailPrintClasses}"><thead>${renderDetailTableHeaderRows(block)}</thead><tbody>${renderDetailTableRows(block, detailRow)}${summaryRow}</tbody></table>`;

  if (!block.sideBand) {
    return table;
  }

  const sideContent = block.sideBand.contentKind === "Field"
    ? renderFieldExpression(block.sideBand.fieldPath)
    : escapeHtml(block.sideBand.text);

  return `<table class="edm-detail-layout ${renderDetailHeaderRepeatClassName(block)}">
  <thead>
    <tr>
      <th style="${renderDetailLayoutCellStyle(block.headerStyle, block.border, block.sideBand.widthMm)}">${escapeHtml(block.sideBand.title)}</th>
      <th style="${renderDetailLayoutCellStyle(block.headerStyle, block.border, block.detailWidthMm)}">${escapeHtml(block.title || "Detail")}</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td style="${renderDetailLayoutCellStyle(block.sideBand.style, block.border, block.sideBand.widthMm, "top")}">${sideContent}</td>
      <td style="${renderDetailLayoutCellStyle({}, block.border, block.detailWidthMm, "top")}; padding: 0;">${table}</td>
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
    .map((column) => `<th style="${renderDetailCellStyle(block.headerStyle, column.border ?? block.border, column.align, column.widthMm)}">${escapeHtml(column.title)}</th>`)
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
    cells.push(`<th colspan="${columnSpan}" style="${renderDetailCellStyle(block.headerStyle, column.border ?? block.border, column.align)}">${escapeHtml(title)}</th>`);
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

    return `<td style="${renderDetailCellStyle(footer.style, block.border, column.align)}">${content}</td>`;
  }).join("");

  return `<tr class="edm-detail-group-footer-row"><td colspan="${labelColumnSpan}" style="${renderDetailCellStyle(footer.style, block.border, "Right")}">${escapeHtml(footer.label)}</td>${valueCells}</tr>`;
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
  ].join(" ");
}

function renderDetailHeaderRepeatClassName(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  return block.print.repeatHeaderOnPageBreak ? "edm-detail-repeat-header" : "edm-detail-no-repeat-header";
}

function renderDetailTableSummaryRow(block: Extract<ReportBlock, { type: "DetailTable" }>) {
  if (!block.summaryRow) {
    return "";
  }

  const labelColumnSpan = Math.min(block.columns.length, Math.max(1, Math.floor(block.summaryRow.labelColumnSpan)));
  const cellsByColumnId = new Map(block.summaryRow.cells.map((cell) => [cell.columnId, cell]));
  const valueCells = block.columns.slice(labelColumnSpan).map((column) => {
    const cell = cellsByColumnId.get(column.id);
    const content = cell ? renderSummaryCellContent(cell) : "";

    return `<td style="${renderDetailCellStyle(block.summaryRow!.style, block.border, column.align)}">${content}</td>`;
  }).join("");

  return `<tr class="edm-detail-summary-row"><td colspan="${labelColumnSpan}" style="${renderDetailCellStyle(block.summaryRow.style, block.border, "Right")}">${escapeHtml(block.summaryRow.label)}</td>${valueCells}</tr>`;
}

function renderSummaryCellContent(cell: NonNullable<Extract<ReportBlock, { type: "DetailTable" }>["summaryRow"]>["cells"][number]) {
  switch (cell.contentKind) {
    case "Field":
      return renderFieldExpression(cell.fieldPath);
    case "Text":
      return escapeHtml(cell.text);
    case "Empty":
      return "";
  }
}

function renderDetailCellContent(column: Extract<ReportBlock, { type: "DetailTable" }>["columns"][number]) {
  if (column.contentKind !== "Composite" || !column.content || column.content.length === 0) {
    return renderDetailItemExpression(column.fieldPath);
  }

  return column.content.map((part) => {
    switch (part.kind) {
      case "Text":
        return escapeHtml(part.text);
      case "Field":
        return renderDetailItemExpression(part.fieldPath);
      case "LineBreak":
        return "<br>";
    }
  }).join("");
}

function renderDetailLayoutCellStyle(
  style: ReportTextStyle,
  border: ReportBorderStyle,
  widthMm?: number,
  verticalAlign?: "top" | "middle" | "bottom",
) {
  return [
    widthMm ? `width: ${widthMm}mm` : "",
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
  widthMm?: number,
) {
  return [
    widthMm ? `width: ${widthMm}mm` : "",
    `text-align: ${alignToCss(align)}`,
    style.fontSizePt ? `font-size: ${style.fontSizePt}pt` : "",
    style.bold ? "font-weight: 700" : "",
    style.marginTopMm ? `padding-top: ${style.marginTopMm}mm` : "",
    style.marginRightMm ? `padding-right: ${style.marginRightMm}mm` : "",
    style.marginBottomMm ? `padding-bottom: ${style.marginBottomMm}mm` : "",
    style.marginLeftMm ? `padding-left: ${style.marginLeftMm}mm` : "",
    "overflow-wrap: anywhere",
    "word-break: break-word",
    renderBorderStyle(border),
  ].filter(Boolean).join("; ");
}

function readDetailItemField(fieldPath: string) {
  const normalized = fieldPath.trim();
  if (normalized.startsWith("Invoice.Items.")) {
    return normalized.slice("Invoice.Items.".length);
  }

  if (normalized.startsWith("item.")) {
    return normalized.slice("item.".length);
  }

  return normalized.split(".").pop() ?? normalized;
}

function renderDetailItemExpression(fieldPath: string) {
  const itemReference = renderDetailItemValueReference(fieldPath);
  return itemReference ? `{{ ${itemReference} }}` : "";
}

function renderDetailItemValueReference(fieldPath: string) {
  const itemField = readDetailItemField(fieldPath);
  return isReportDesignerFieldPath(itemField) ? `item.${itemField}` : "";
}

function renderFieldExpression(fieldPath: string, fallbackText?: string) {
  const normalized = fieldPath.trim();
  if (!isReportDesignerFieldPath(normalized)) return escapeHtml(fallbackText ?? "");
  const expression = `{{ ${normalized} }}`;
  return fallbackText ? `{{ if ${normalized} }}${expression}{{ else }}${escapeHtml(fallbackText)}{{ end }}` : expression;
}

function renderTextStyle(style: ReportTextStyle) {
  return [
    style.fontSizePt ? `font-size: ${style.fontSizePt}pt` : "",
    style.bold ? "font-weight: 700" : "",
    style.align ? `text-align: ${alignToCss(style.align)}` : "",
    style.marginTopMm ? `margin-top: ${style.marginTopMm}mm` : "",
    style.marginRightMm ? `margin-right: ${style.marginRightMm}mm` : "",
    style.marginBottomMm ? `margin-bottom: ${style.marginBottomMm}mm` : "",
    style.marginLeftMm ? `margin-left: ${style.marginLeftMm}mm` : "",
  ].filter(Boolean).join("; ");
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
