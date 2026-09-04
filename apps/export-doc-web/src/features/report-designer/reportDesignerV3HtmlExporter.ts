import type { ReportBlock } from "./reportDesignerSchema.ts";
import { renderReportDesignerBlockToHtml } from "./reportDesignerBlockRenderer.ts";
import {
  hundredthMmToMm,
  reportDesignerV3ElementBounds,
  reportDesignerV3PageSize,
  type ReportDesignerV3Element,
  type ReportDesignerV3Schema,
} from "./reportDesignerV3Schema.ts";
import {
  hasBlockingReportDesignerV3SchemaIssues,
  normalizeReportDesignerV3Schema,
} from "./reportDesignerV3Validation.ts";
import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";
import { isControlledReportImageFieldPath } from "./reportDesignerSchemaDomains.ts";

const fieldPathPattern = /^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$/;
const colorPattern = /^#[0-9a-fA-F]{3,8}$/;
const fontFamilyPattern = /^[A-Za-z0-9 \t"',._-]+$/;
const resourceIdPattern = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,159}$/;

/**
 * Runs the same normalization/domain gate used by the exporter without
 * producing HTML.  The workspace uses this result to keep an invalid draft
 * out of the parent form instead of treating an empty export as valid data.
 */
export function validateReportDesignerV3Export(
  schema: ReportDesignerV3Schema,
  expectedReportType?: ReportDesignerReportType,
) {
  const validation = normalizeReportDesignerV3Schema(schema, expectedReportType);
  return {
    ...validation,
    blocked: !validation.schema || hasBlockingReportDesignerV3SchemaIssues(validation.issues),
  };
}

export function exportReportDesignerV3SchemaToHtml(
  schema: ReportDesignerV3Schema,
  expectedReportType?: ReportDesignerReportType,
) {
  const validation = validateReportDesignerV3Export(schema, expectedReportType);
  // A V3 draft with blocking schema/domain errors must never become writable
  // HTML.  Returning an empty draft keeps the original template selected in
  // the workspace and makes the save action unavailable until the user fixes
  // the structure explicitly.
  if (validation.blocked || !validation.schema) {
    return "";
  }
  const normalized = validation.schema;

  const page = reportDesignerV3PageSize(normalized.page);
  const visibleLayers = normalized.layers.filter((layer) => layer.visible);
  const staticLayers = visibleLayers.map((layer) => renderStaticLayer(layer, normalized.page)).join("\n");
  const flowElements = visibleLayers
    .filter((layer) => layer.role === "Body")
    .flatMap((layer) => layer.elements.filter((element): element is Extract<ReportDesignerV3Element, { type: "Flow" }> => element.type === "Flow" && element.visible && element.outputEnabled))
    .sort((left, right) => left.yHundredthMm - right.yHundredthMm || left.zIndex - right.zIndex);
  const headerReserve = measureRepeatedLayerReserve(visibleLayers, "Header", normalized.page.heightHundredthMm);
  const footerReserve = measureRepeatedLayerReserve(visibleLayers, "Footer", normalized.page.heightHundredthMm);
  const flowStream = renderFlowStream(flowElements, headerReserve, footerReserve);
  const repeatedLayers = visibleLayers
    .filter((layer) => layer.print.repeatOnEveryPage)
    .map((layer) => renderRepeatedLayer(layer, normalized.page))
    .join("\n");
  const schemaComment = serializeSchemaComment(normalized);

  return `<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    @page { size: ${page.widthMm}mm ${page.heightMm}mm; margin: 0; }
    html, body { margin: 0; padding: 0; }
    body { font-family: ${renderFontFamily(normalized.page.fontFamily)}; font-size: ${normalized.page.fontSizePt}pt; color: #1f2933; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
    *, *::before, *::after { box-sizing: border-box; }
    .edm-v3-page { position: relative; width: ${page.widthMm}mm; min-height: ${page.heightMm}mm; margin: 0 auto; overflow: visible; background: #fff; }
    .edm-v3-static-canvas { position: absolute; inset: 0; width: ${page.widthMm}mm; height: ${page.heightMm}mm; overflow: hidden; pointer-events: none; }
    .edm-v3-flow-stream { position: relative; z-index: 1; width: ${page.widthMm}mm; min-height: ${page.heightMm}mm; border-collapse: collapse; table-layout: fixed; }
    .edm-v3-flow-stream > thead { display: table-header-group; }
    .edm-v3-flow-stream > tfoot { display: table-footer-group; }
    .edm-v3-flow-stream > thead td, .edm-v3-flow-stream > tfoot td { padding: 0; border: 0; }
    .edm-v3-flow-stream > tbody > tr > td { padding: 0; border: 0; vertical-align: top; }
    .edm-v3-flow-items { display: flow-root; min-height: ${page.heightMm}mm; }
    .edm-v3-flow-item { position: relative; box-sizing: border-box; max-width: 100%; }
    .edm-v3-flow-item-row, .edm-v3-flow-item-grid, .edm-v3-flow-item-conditional, .edm-v3-flow-item-pagebreak { break-inside: avoid; page-break-inside: avoid; }
    .edm-v3-flow-item-detailtable { break-inside: auto; page-break-inside: auto; }
    .edm-v3-layer-keep-together { break-inside: avoid; page-break-inside: avoid; }
    .edm-v3-repeat-layer { position: fixed; inset: 0; width: ${page.widthMm}mm; height: ${page.heightMm}mm; overflow: hidden; pointer-events: none; z-index: 1000; }
    .edm-v3-repeat-layer .edm-v3-element { pointer-events: none; }
    .edm-v3-element { position: absolute; overflow: visible; min-width: 0; min-height: 0; }
    .edm-v3-text, .edm-v3-field { overflow-wrap: anywhere; white-space: pre-wrap; word-break: break-word; }
    .edm-v3-flow { overflow: visible; }
    /* Row/Grid/Conditional Flow elements outside Body are intentionally
       rendered as fixed layer content.  Repetition follows the owning layer;
       pagination-producing DetailTable/PageBreak elements are rejected by
       schema validation instead of receiving ambiguous output semantics. */
    .edm-v3-flow-static { overflow: visible; }
    .edm-v3-flow > table, .edm-v3-flow > section { max-width: 100%; }
    .edm-v3-image { display: block; width: 100%; height: 100%; object-fit: contain; }
    .edm-v3-page-number { white-space: pre; }
    .edm-v3-page-number-overlay { position: absolute; left: 0; top: 0; pointer-events: none; z-index: 2000; }
    .edm-v3-image-placeholder { display: grid; place-items: center; width: 100%; height: 100%; padding: 1mm; color: #64748b; border: 1px dashed #94a3b8; font-size: 8pt; text-align: center; overflow-wrap: anywhere; }
    .edm-v3-rectangle { width: 100%; height: 100%; }
    .edm-v3-line { position: absolute; background: #334155; }
    .edm-v3-line-horizontal { left: 0; right: 0; top: 50%; height: 1px; transform: translateY(-50%); }
    .edm-v3-line-vertical { top: 0; bottom: 0; left: 50%; width: 1px; transform: translateX(-50%); }
    .edm-report-row { width: 100%; border-collapse: collapse; table-layout: fixed; }
    .edm-report-row td, .edm-report-grid td, .edm-report-grid th, .edm-detail-table td, .edm-detail-table th { overflow-wrap: anywhere; word-break: break-word; white-space: pre-wrap; }
    .edm-report-grid, .edm-detail-table, .edm-detail-layout { width: 100%; border-collapse: collapse; table-layout: fixed; }
    .edm-report-grid td, .edm-report-grid th, .edm-detail-table td, .edm-detail-table th, .edm-detail-layout td, .edm-detail-layout th { border: 1px solid #333; padding: 1mm; vertical-align: top; }
    .edm-detail-table thead { display: table-header-group; }
    .edm-detail-table tr { page-break-inside: avoid; break-inside: avoid; }
    .report-page-break-row { page-break-before: always; break-before: page; height: 0; }
    @media screen { .edm-v3-repeat-layer { display: none; } }
    @media print {
      .edm-v3-page { margin: 0; }
      .edm-v3-repeat-source { visibility: hidden; }
      .edm-v3-repeat-layer { display: block; }
    }
  </style>
</head>
<body>
<!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA
${schemaComment}
-->
<div class="edm-v3-page">
  <div class="edm-v3-static-canvas">${staticLayers}</div>
  ${flowStream}
</div>
${repeatedLayers}
</body>
</html>`;
}

function renderStaticLayer(
  layer: ReportDesignerV3Schema["layers"][number],
  page: ReportDesignerV3Schema["page"],
) {
  const className = [
    "edm-v3-layer",
    `edm-v3-layer-${layer.role.toLowerCase()}`,
    layer.print.repeatOnEveryPage ? "edm-v3-repeat-source" : "",
    layer.print.keepTogether ? "edm-v3-layer-keep-together" : "",
    layer.print.pinToPageBottom && layer.role === "Footer" ? "edm-v3-layer-pin-footer" : "",
  ].filter(Boolean).join(" ");
  const style = layer.print.minHeightHundredthMm > 0
    ? `min-height: ${hundredthMmToMm(layer.print.minHeightHundredthMm)}mm`
    : "";
  const yOffset = getFooterPinOffset(layer, page);
  const elements = [...layer.elements]
    .filter((element) => element.visible && element.outputEnabled && !(layer.role === "Body" && element.type === "Flow"))
    .sort((left, right) => left.zIndex - right.zIndex)
    .map((element) => renderElement(element, yOffset, layer.role !== "Body"))
    .join("\n");
  return `<div class="${className}"${style ? ` style="${style}"` : ""}>${elements}</div>`;
}

function renderRepeatedLayer(
  layer: ReportDesignerV3Schema["layers"][number],
  page: ReportDesignerV3Schema["page"],
) {
  const className = [
    "edm-v3-repeat-layer",
    `edm-v3-repeat-layer-${layer.role.toLowerCase()}`,
    layer.print.keepTogether ? "edm-v3-layer-keep-together" : "",
    layer.print.pinToPageBottom && layer.role === "Footer" ? "edm-v3-layer-pin-footer" : "",
  ].filter(Boolean).join(" ");
  const yOffset = getFooterPinOffset(layer, page);
  const elements = [...layer.elements]
    .filter((element) => element.visible && element.outputEnabled)
    .sort((left, right) => left.zIndex - right.zIndex)
    .map((element) => renderElement(element, yOffset, layer.role !== "Body"))
    .join("\n");
  return `<div class="${className}">${elements}</div>`;
}

function getFooterPinOffset(
  layer: ReportDesignerV3Schema["layers"][number],
  page: ReportDesignerV3Schema["page"],
) {
  if (layer.role !== "Footer" || !layer.print.pinToPageBottom) return 0;
  const visibleElements = layer.elements.filter((element) => element.visible && element.outputEnabled);
  const visualBounds = visibleElements.map((element) => reportDesignerV3ElementBounds(element));
  const contentBottom = visualBounds.length > 0
    ? Math.max(...visualBounds.map((bounds) => bounds.bottom))
    : page.heightHundredthMm;
  // Align the actual footer content's bottom edge with the physical page
  // bottom.  minHeight is a reservation hint for the flow spacer, not an
  // instruction to leave extra blank space below the visible content.
  return page.heightHundredthMm - contentBottom;
}

function renderFlowStream(
  elements: Array<Extract<ReportDesignerV3Element, { type: "Flow" }>>,
  headerReserveHundredthMm: number,
  footerReserveHundredthMm: number,
) {
  if (elements.length === 0) return "";
  let previousBottom = 0;
  const items = elements.map((element) => {
    const gap = Math.max(0, element.yHundredthMm - previousBottom - (previousBottom === 0 ? headerReserveHundredthMm : 0));
    previousBottom = Math.max(previousBottom, element.yHundredthMm + element.heightHundredthMm);
    const style = [
      `margin-top: ${hundredthMmToMm(gap)}mm`,
      `margin-left: ${hundredthMmToMm(element.xHundredthMm)}mm`,
      `width: ${hundredthMmToMm(element.widthHundredthMm)}mm`,
      `min-height: ${hundredthMmToMm(element.heightHundredthMm)}mm`,
      `z-index: ${element.zIndex}`,
      element.rotationDeg ? `transform: rotate(${element.rotationDeg}deg)` : "",
    ].filter(Boolean).join("; ");
    return `<div class="edm-v3-flow-item edm-v3-flow-item-${element.flowKind.toLowerCase()}" style="${style}">${renderElementContent(element)}</div>`;
  }).join("\n");
  const headerHeight = hundredthMmToMm(headerReserveHundredthMm);
  const footerHeight = hundredthMmToMm(footerReserveHundredthMm);
  return `<table class="edm-v3-flow-stream"><thead><tr><td style="height: ${headerHeight}mm"></td></tr></thead><tbody><tr><td><div class="edm-v3-flow-items">${items}</div></td></tr></tbody><tfoot><tr><td style="height: ${footerHeight}mm"></td></tr></tfoot></table>`;
}

function measureRepeatedLayerReserve(
  layers: ReportDesignerV3Schema["layers"],
  role: ReportDesignerV3Schema["layers"][number]["role"],
  pageHeightHundredthMm: number,
) {
  const repeated = layers.filter((layer) => layer.role === role && layer.print.repeatOnEveryPage);
  if (repeated.length === 0) return 0;
  if (role === "Header") {
    return Math.min(
      Math.floor(pageHeightHundredthMm * 0.35),
       Math.max(...repeated.map((layer) => Math.max(
         layer.print.minHeightHundredthMm,
         ...layer.elements
           .filter((element) => element.visible && element.outputEnabled)
           .map((element) => reportDesignerV3ElementBounds(element).bottom),
         0,
       ))),
    );
  }
  return Math.min(
    Math.floor(pageHeightHundredthMm * 0.35),
    Math.max(...repeated.map((layer) => {
      const visibleElements = layer.elements.filter((element) => element.visible && element.outputEnabled);
      if (layer.print.pinToPageBottom) {
        const visualBounds = visibleElements.map((element) => reportDesignerV3ElementBounds(element));
        const contentHeight = visibleElements.length === 0
          ? 0
          : Math.max(...visualBounds.map((bounds) => bounds.bottom)) -
            Math.min(...visualBounds.map((bounds) => bounds.top));
        return Math.max(layer.print.minHeightHundredthMm, contentHeight);
      }

      return Math.max(
        layer.print.minHeightHundredthMm,
        ...visibleElements.map((element) => pageHeightHundredthMm - reportDesignerV3ElementBounds(element).top),
        0,
      );
    })),
  );
}

function renderElement(element: ReportDesignerV3Element, yOffset = 0, staticFlow = false) {
  const style = renderElementPositionStyle(element, yOffset);
  const className = [
    "edm-v3-element",
    `edm-v3-element-${element.type.toLowerCase()}`,
    staticFlow && element.type === "Flow" ? "edm-v3-flow-static" : "",
  ].filter(Boolean).join(" ");
  return `<div class="${className}" style="${style}">${renderElementContent(element)}</div>`;
}

function renderElementContent(element: ReportDesignerV3Element) {
  return (() => {
    switch (element.type) {
      case "Text":
        return `<div class="edm-v3-text" style="${renderTextStyle(element)}">${escapeHtml(element.text)}</div>`;
      case "Field":
        return `<div class="edm-v3-field" style="${renderTextStyle(element)}">${element.label ? `${escapeHtml(element.label)}: ` : ""}${renderField(element.fieldPath, element.fallbackText)}</div>`;
      case "Image":
        return renderImage(element);
      case "PageNumber":
        return renderPageNumber(element);
      case "Rectangle":
        return `<div class="edm-v3-rectangle" style="${renderBoxStyle(element)}"></div>`;
      case "Line":
        return `<div class="edm-v3-line edm-v3-line-${element.direction.toLowerCase()}" style="${renderLineStyle(element)}"></div>`;
      case "Flow":
        return `<div class="edm-v3-flow">${renderReportDesignerBlockToHtml(element.block as ReportBlock)}</div>`;
    }
  })();
}

function renderImage(element: Extract<ReportDesignerV3Element, { type: "Image" }>) {
  const fieldPath = element.fieldPath?.trim();
  if (element.sourceKind === "Field" && isControlledReportImageFieldPath(fieldPath)) {
    const src = `{{ ${fieldPath} }}`;
    const image = `<img class="edm-v3-image" src="${src}" alt="${escapeHtmlAttribute(element.altText ?? "")}">`;
    return element.hideWhenSourceEmpty ? `{{ if ${fieldPath} }}${image}{{ end }}` : image;
  }

  const requestedResource = element.resourceId?.trim();
  const resource = requestedResource && resourceIdPattern.test(requestedResource) ? requestedResource : undefined;
  return resource
    ? `<img class="edm-v3-image" data-edm-v3-resource-id="${escapeHtmlAttribute(resource)}" alt="${escapeHtmlAttribute(element.altText ?? "")}">`
    : `<div class="edm-v3-image-placeholder" role="img" aria-label="${escapeHtmlAttribute(element.altText ?? "未上传图片")}">图片资源未上传</div>`;
}

function renderPageNumber(element: Extract<ReportDesignerV3Element, { type: "PageNumber" }>) {
  const prefix = escapeHtml(element.prefix ?? "");
  const suffix = escapeHtml(element.suffix ?? "");
  const current = `<span class="edm-v3-page-number-current" data-edm-v3-page-number-current>1</span>`;
  const total = `<span class="edm-v3-page-number-total" data-edm-v3-page-number-total>1</span>`;
  return `<span class="edm-v3-page-number" data-edm-v3-page-number>${prefix}${current}${element.format === "CurrentOfTotal" ? ` / ${total}` : ""}${suffix}</span>`;
}

function renderElementPositionStyle(element: ReportDesignerV3Element, yOffset = 0) {
  return [
    `left: ${hundredthMmToMm(element.xHundredthMm)}mm`,
    `top: ${hundredthMmToMm(element.yHundredthMm + yOffset)}mm`,
    `width: ${hundredthMmToMm(element.widthHundredthMm)}mm`,
    `height: ${hundredthMmToMm(element.heightHundredthMm)}mm`,
    `z-index: ${element.zIndex}`,
    element.rotationDeg ? `transform: rotate(${element.rotationDeg}deg)` : "",
  ].filter(Boolean).join("; ");
}

function renderTextStyle(element: Extract<ReportDesignerV3Element, { type: "Text" | "Field" }>) {
  const style = element.style;
  return [
    style.fontFamily ? `font-family: ${renderFontFamily(style.fontFamily)}` : "",
    style.fontSizePt ? `font-size: ${style.fontSizePt}pt` : "",
    style.bold ? "font-weight: 700" : "",
    style.color && colorPattern.test(style.color) ? `color: ${style.color}` : "",
    style.backgroundColor && colorPattern.test(style.backgroundColor) ? `background-color: ${style.backgroundColor}` : "",
    style.align ? `text-align: ${style.align.toLowerCase()}` : "",
    style.paddingHundredthMm ? `padding: ${hundredthMmToMm(style.paddingHundredthMm)}mm` : "",
    renderBorder(style),
  ].filter(Boolean).join("; ");
}

function renderBoxStyle(element: Extract<ReportDesignerV3Element, { type: "Rectangle" }>) {
  return [
    element.style.backgroundColor && colorPattern.test(element.style.backgroundColor) ? `background-color: ${element.style.backgroundColor}` : "",
    renderBorder(element.style),
  ].filter(Boolean).join("; ");
}

function renderLineStyle(element: Extract<ReportDesignerV3Element, { type: "Line" }>) {
  const style = element.style;
  if (style.borderStyle === "None") return "display: none";
  const color = style.borderColor && colorPattern.test(style.borderColor) ? style.borderColor : "#334155";
  const widthPx = Math.max(1, Math.min(8, style.borderWidthPx ?? 1));
  if (style.borderStyle === "Dashed") {
    return element.direction === "Horizontal"
      ? `height: 0; border-top: ${widthPx}px dashed ${color}`
      : `width: 0; border-left: ${widthPx}px dashed ${color}`;
  }
  return element.direction === "Horizontal"
    ? `height: ${widthPx}px; background-color: ${color}`
    : `width: ${widthPx}px; background-color: ${color}`;
}

function renderBorder(style: ReportDesignerV3Element["style"]) {
  if (!style.borderWidthPx || style.borderWidthPx <= 0 || style.borderStyle === "None") return "";
  const color = style.borderColor && colorPattern.test(style.borderColor) ? style.borderColor : "#334155";
  const borderStyle = style.borderStyle === "Dashed" ? "dashed" : "solid";
  return `border: ${style.borderWidthPx}px ${borderStyle} ${color}`;
}

function renderField(fieldPath: string, fallback?: string) {
  if (!isFieldPath(fieldPath)) return escapeHtml(fallback ?? "");
  const expression = `{{ ${fieldPath.trim()} }}`;
  return fallback ? `{{ if ${fieldPath.trim()} }}${expression}{{ else }}${escapeHtml(fallback)}{{ end }}` : expression;
}

function isFieldPath(value: string) {
  return fieldPathPattern.test(value.trim());
}

function renderFontFamily(value: string) {
  return value.split(",").map((part) => fontFamilyPattern.test(part.trim()) ? part.trim() : "Arial").join(", ");
}

function escapeHtml(value: string) {
  return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

function escapeHtmlAttribute(value: string) {
  return escapeHtml(value).replace(/\"/g, "&quot;");
}

function serializeSchemaComment(schema: ReportDesignerV3Schema) {
  // HTML comments cannot contain a double hyphen.  Escape the characters in
  // the JSON representation (rather than changing the parsed value) so user
  // text such as "--" or "-->" cannot terminate the schema comment and break
  // the V3 round-trip parser.
  return JSON.stringify(schema, null, 2)
    .replace(/--/g, "-\\u002d")
    .replace(/</g, "\\u003c");
}
