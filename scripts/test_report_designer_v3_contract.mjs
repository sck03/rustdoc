import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspaceRoot = path.join(repoRoot, ".codex-runtime", "report-designer-v3-contract");
const entryPath = path.join(workspaceRoot, "entry.ts");
const bundlePath = path.join(workspaceRoot, "bundle.mjs");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));

fs.mkdirSync(workspaceRoot, { recursive: true });
const sourceRoot = path.join(repoRoot, "apps/export-doc-web/src/features/report-designer");
const workspaceSource = fs.readFileSync(path.join(sourceRoot, "ReportDesignerV3Workspace.tsx"), "utf8");
const importSpecifier = (name) => {
  const relative = path.relative(workspaceRoot, path.join(sourceRoot, name)).replaceAll("\\", "/");
  return relative.startsWith(".") ? relative : `./${relative}`;
};
fs.writeFileSync(entryPath, `
export * from ${JSON.stringify(importSpecifier("reportDesignerV3Migration.ts"))};
export * from ${JSON.stringify(importSpecifier("reportDesignerV3Validation.ts"))};
export * from ${JSON.stringify(importSpecifier("reportDesignerV3Schema.ts"))};
export * from ${JSON.stringify(importSpecifier("reportDesignerV3Mutations.ts"))};
export * from ${JSON.stringify(importSpecifier("reportDesignerV3TemplateParser.ts"))};
export * from ${JSON.stringify(importSpecifier("reportDesignerV3TemplateAnalysis.ts"))};
export * from ${JSON.stringify(importSpecifier("reportDesignerV3HtmlExporter.ts"))};
`);
await esbuild.build({ entryPoints: [entryPath], outfile: bundlePath, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
const api = await import(pathToFileURL(bundlePath).href);
const assert = (condition, message) => { if (!condition) throw new Error(message); };

const legacyA5 = {
  version: 2,
  reportType: "ExportDocument",
  page: { size: "A5", orientation: "Landscape", marginTopMm: 8, marginRightMm: 8, marginBottomMm: 8, marginLeftMm: 8, fontFamily: "Arial", fontSizePt: 9 },
  sections: [{ id: "body", type: "Body", print: { repeatOnEveryPage: false, keepTogether: false }, blocks: [{ id: "title", type: "Text", text: "标题", style: { fontSizePt: 12 } }] }],
};
const migrated = api.migrateReportDesignerSchemaV2ToV3(legacyA5);
assert(migrated.schema.page.size === "A4", "迁移后页面必须固定为 A4");
assert(migrated.schema.page.widthHundredthMm === 29700 && migrated.schema.page.heightHundredthMm === 21000, "横版 A4 尺寸错误");
assert(migrated.issues.some((issue) => issue.path === "$.page.size"), "非 A4 迁移必须给出明确提示");

const crossDomainLegacy = api.migrateReportDesignerSchemaV2ToV3(
  { ...legacyA5, reportType: "PaymentVoucher" },
  "ExportDocument",
);
assert(crossDomainLegacy.schema.reportType === "ExportDocument", "V2 迁移必须以当前路由数据域为权威");
assert(crossDomainLegacy.issues.some((issue) => issue.severity === "error" && issue.path === "$.reportType"), "跨域 V2 迁移必须阻断并要求人工修正");

const legacyStyled = api.migrateReportDesignerSchemaV2ToV3({
  ...legacyA5,
  page: { ...legacyA5.page, size: "A4", orientation: "Portrait" },
  sections: [{
    id: "body-styled",
    type: "Body",
    print: { repeatOnEveryPage: false, keepTogether: false },
    blocks: [{
      id: "styled-text",
      type: "Text",
      text: "带边框",
      style: { fontSizePt: 11, bold: true, align: "Center" },
      border: { color: "#112233", widthPx: 2, style: "Dashed", top: true, right: true, bottom: false, left: true },
    }],
  }],
});
const styledElement = legacyStyled.schema.layers.find((layer) => layer.role === "Body").elements[0];
assert(styledElement.style.borderColor === "#112233" && styledElement.style.borderStyle === "Dashed", "V2 边框样式迁移必须保留");

const normalized = api.normalizeReportDesignerV3Schema({ ...migrated.schema, page: { ...migrated.schema.page, size: "Custom", widthHundredthMm: 99999, heightHundredthMm: 99999 } });
assert(normalized.schema?.page.size === "A4", "v3 校验不得保留非 A4 页面");
assert(normalized.schema?.page.widthHundredthMm === 29700 && normalized.schema?.page.heightHundredthMm === 21000, "v3 校验必须恢复标准横版 A4 尺寸");

const maliciousFlow = api.normalizeReportDesignerV3Schema({
  ...migrated.schema,
  layers: [{
    ...migrated.schema.layers[0],
    elements: [{
      ...migrated.schema.layers[0].elements[0],
      type: "Flow",
      flowKind: "Conditional",
      block: {
        id: "unsafe-flow",
        type: "Conditional",
        condition: { fieldPath: "Invoice.Nope;danger", operator: "HasValue", value: "" },
        content: { kind: "Text", text: "unsafe", fieldPath: "" },
        style: {},
      },
    }],
  }, ...migrated.schema.layers.slice(1)],
});
assert(maliciousFlow.issues.some((issue) => issue.severity === "error" && issue.path.includes("block")), "Flow 内嵌表达式必须经过统一白名单校验");

const manyElementsLayer = {
  ...migrated.schema.layers[0],
  elements: Array.from({ length: api.REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER + 20 }, (_, index) => ({
    ...migrated.schema.layers[0].elements[0],
    id: `many-${index}`,
    type: "Text",
    text: String(index),
  })),
};
const capped = api.normalizeReportDesignerV3Schema({ ...migrated.schema, layers: [manyElementsLayer, ...migrated.schema.layers.slice(1)] });
assert(capped.schema?.layers[0].elements.length === api.REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER, "图层元素上限必须实际截断");

const legacyOverflow = api.migrateReportDesignerSchemaV2ToV3({
  ...legacyA5,
  sections: [{
    ...legacyA5.sections[0],
    blocks: Array.from({ length: api.REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER + 25 }, (_, index) => ({
      id: `legacy-overflow-${index}`,
      type: "Text",
      text: String(index),
    })),
  }],
});
assert(legacyOverflow.schema.layers[0].elements.length === api.REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER, "V2 迁移必须在构造元素前遵守单图层预算");
assert(legacyOverflow.issues.some((issue) => issue.message.includes("单图层最多迁移")), "V2 超量迁移必须给出明确预算提示");

const legacyTotalOverflow = api.migrateReportDesignerSchemaV2ToV3({
  ...legacyA5,
  sections: Array.from({ length: 5 }, (_, sectionIndex) => ({
    ...legacyA5.sections[0],
    id: `legacy-total-${sectionIndex}`,
    blocks: Array.from({ length: api.REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER }, (_, index) => ({
      id: `legacy-total-${sectionIndex}-${index}`,
      type: "Text",
      text: String(index),
    })),
  })),
});
const legacyTotalCount = legacyTotalOverflow.schema.layers.reduce((sum, layer) => sum + layer.elements.length, 0);
assert(legacyTotalCount <= api.REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS, "V2 迁移必须遵守总元素预算");
assert(legacyTotalOverflow.issues.some((issue) => issue.message.includes("元素总数已达到")), "V2 总预算耗尽必须给出明确提示");

const manyLayers = Array.from({ length: api.REPORT_DESIGNER_V3_MAX_LAYER_COUNT + 4 }, (_, index) => ({
  ...migrated.schema.layers[0],
  id: `layer-cap-${index}`,
  elements: [],
}));
const cappedLayers = api.normalizeReportDesignerV3Schema({ ...migrated.schema, layers: manyLayers });
assert(cappedLayers.schema?.layers.length <= api.REPORT_DESIGNER_V3_MAX_LAYER_COUNT, "图层上限不能生成第 17 层或更多图层");

let state = api.createReportDesignerV3DocumentState(migrated.schema);
const layerId = state.activeLayerId;
assert(layerId, "迁移结果应有活动图层");
const first = state.schema.layers[0].elements[0];
const selectedAgain = api.toggleV3Selection({ ...state, selectedIds: [first.id] }, first.id, false);
assert(selectedAgain.selectedIds.length === 1 && selectedAgain.selectedIds[0] === first.id, "普通点击当前选中元素不得意外清空选择");
const toggledOff = api.toggleV3Selection(selectedAgain, first.id, true);
assert(toggledOff.selectedIds.length === 0, "Shift/Ctrl/⌘ 点击已选元素应明确切换选择");
state = api.insertV3Element(state, layerId, api.createV3TextElement(29000, 20500));
const insertedId = state.selectedIds[0];
const inserted = api.findV3Element(state.schema, insertedId);
assert(inserted?.element.xHundredthMm + inserted.element.widthHundredthMm <= 29700, "新增元素不得越出横版页面");
const layerIdCollision = api.insertV3Element(state, layerId, { ...api.createV3TextElement(), id: state.schema.layers[1].id });
assert(layerIdCollision.selectedIds[0] !== state.schema.layers[1].id, "新增元素 ID 不得与图层 ID 冲突");
state = { ...state, selectedIds: [first.id, insertedId] };
const moved = api.moveSelectedV3Elements(state, 20000, 20000, false);
for (const id of moved.selectedIds) {
  const found = api.findV3Element(moved.schema, id);
  assert(found.element.xHundredthMm + found.element.widthHundredthMm <= 29700 && found.element.yHundredthMm + found.element.heightHundredthMm <= 21000, "多选移动必须整体限制在页面内");
}
const lockedState = api.updateV3Element(moved, first.id, { locked: true });
const lockedMoved = api.moveSelectedV3Elements({ ...lockedState, selectedIds: [first.id] }, 500, 500, false);
assert(api.findV3Element(lockedMoved.schema, first.id).element.xHundredthMm === api.findV3Element(lockedState.schema, first.id).element.xHundredthMm, "锁定元素不得移动");
const resized = api.resizeV3Element({ ...moved, selectedIds: [insertedId] }, insertedId, "se", -50000, -50000);
const resizedElement = api.findV3Element(resized.schema, insertedId).element;
assert(resizedElement.widthHundredthMm >= 400 && resizedElement.heightHundredthMm >= 400, "缩放必须保留最小尺寸");
const rotatedResizeElement = {
  ...api.createV3TextElement(5000, 5000),
  id: "rotated-resize",
  widthHundredthMm: 4000,
  heightHundredthMm: 1000,
  rotationDeg: 45,
};
const rotatedResizeSchema = {
  ...migrated.schema,
  layers: migrated.schema.layers.map((layer, index) => index === 0
    ? { ...layer, elements: [rotatedResizeElement] }
    : { ...layer, elements: [] }),
};
const rotatedResizeState = { ...api.createReportDesignerV3DocumentState(rotatedResizeSchema), selectedIds: [rotatedResizeElement.id] };
const rotatedResized = api.resizeV3Element(rotatedResizeState, rotatedResizeElement.id, "e", 707, 707);
const rotatedResizedElement = api.findV3Element(rotatedResized.schema, rotatedResizeElement.id).element;
assert(rotatedResizedElement.widthHundredthMm >= rotatedResizeElement.widthHundredthMm + 990, "旋转元素沿自身东侧手柄缩放必须沿局部轴增加宽度");
assert(rotatedResizedElement.heightHundredthMm === rotatedResizeElement.heightHundredthMm, "旋转元素单边水平缩放不得意外改变高度");
const rotatedResizeBounds = api.reportDesignerV3ElementBounds(rotatedResizedElement);
assert(rotatedResizeBounds.left >= -1 && rotatedResizeBounds.top >= -1 && rotatedResizeBounds.right <= migrated.schema.page.widthHundredthMm + 1 && rotatedResizeBounds.bottom <= migrated.schema.page.heightHundredthMm + 1, "旋转元素缩放后视觉边界必须保持在 A4 内");
const extremeRatio = {
  ...rotatedResizeElement,
  id: "rotated-extreme-ratio",
  xHundredthMm: 12000,
  yHundredthMm: 8000,
  widthHundredthMm: 18000,
  heightHundredthMm: 500,
  rotationDeg: 89,
};
const extremeRatioState = {
  ...api.createReportDesignerV3DocumentState({ ...rotatedResizeSchema, layers: rotatedResizeSchema.layers.map((layer, index) => index === 0 ? { ...layer, elements: [extremeRatio] } : { ...layer, elements: [] }) }),
  selectedIds: [extremeRatio.id],
};
const extremeRatioResized = api.resizeV3Element(extremeRatioState, extremeRatio.id, "sw", -50000, 50000);
const extremeRatioElement = api.findV3Element(extremeRatioResized.schema, extremeRatio.id).element;
const extremeRatioBounds = api.reportDesignerV3ElementBounds(extremeRatioElement);
assert(extremeRatioElement.widthHundredthMm >= 400 && extremeRatioElement.heightHundredthMm >= 400, "极端宽高比旋转缩放必须保留最小尺寸");
assert(extremeRatioBounds.left >= -1 && extremeRatioBounds.top >= -1 && extremeRatioBounds.right <= migrated.schema.page.widthHundredthMm + 1 && extremeRatioBounds.bottom <= migrated.schema.page.heightHundredthMm + 1, "极端宽高比旋转缩放必须保持视觉边界");
const rotated = api.clampReportDesignerV3ElementToPage({ ...inserted, rotationDeg: 45 }, migrated.schema.page);
const rotatedBounds = api.reportDesignerV3ElementBounds(rotated);
assert(rotatedBounds.left >= -1 && rotatedBounds.top >= -1 && rotatedBounds.right <= migrated.schema.page.widthHundredthMm + 1 && rotatedBounds.bottom <= migrated.schema.page.heightHundredthMm + 1, "旋转元素的视觉边界必须限制在 A4 页面内");
const oversizedRotated = api.clampReportDesignerV3ElementToPage({
  ...inserted,
  xHundredthMm: -5000,
  yHundredthMm: -5000,
  widthHundredthMm: migrated.schema.page.widthHundredthMm,
  heightHundredthMm: migrated.schema.page.heightHundredthMm,
  rotationDeg: 45,
}, migrated.schema.page);
const oversizedRotatedBounds = api.reportDesignerV3ElementBounds(oversizedRotated);
assert(oversizedRotated.widthHundredthMm < migrated.schema.page.widthHundredthMm && oversizedRotated.heightHundredthMm < migrated.schema.page.heightHundredthMm, "超大旋转元素必须先按视觉包围盒缩放");
assert(oversizedRotatedBounds.left >= -1 && oversizedRotatedBounds.top >= -1 && oversizedRotatedBounds.right <= migrated.schema.page.widthHundredthMm + 1 && oversizedRotatedBounds.bottom <= migrated.schema.page.heightHundredthMm + 1, "超大旋转元素缩放后必须完全位于 A4 页面内");
const rotatedLayoutElements = [
  { ...api.createV3TextElement(1000, 2000), id: "rotated-left", rotationDeg: 45, widthHundredthMm: 3000, heightHundredthMm: 1200 },
  { ...api.createV3TextElement(9000, 7000), id: "rotated-right", rotationDeg: -30, widthHundredthMm: 2600, heightHundredthMm: 1800 },
  { ...api.createV3TextElement(15000, 12000), id: "rotated-third", rotationDeg: 180, widthHundredthMm: 2200, heightHundredthMm: 1400 },
];
const rotatedLayoutSchema = {
  ...migrated.schema,
  layers: migrated.schema.layers.map((layer, index) => index === 0 ? { ...layer, elements: rotatedLayoutElements } : { ...layer, elements: [] }),
};
const rotatedLayoutState = { ...api.createReportDesignerV3DocumentState(rotatedLayoutSchema), selectedIds: rotatedLayoutElements.map((element) => element.id) };
const rotatedMoved = api.moveSelectedV3Elements(rotatedLayoutState, 50000, 50000, false);
for (const id of rotatedMoved.selectedIds) {
  const movedElement = api.findV3Element(rotatedMoved.schema, id).element;
  const movedBounds = api.reportDesignerV3ElementBounds(movedElement);
  assert(movedBounds.left >= -1 && movedBounds.top >= -1 && movedBounds.right <= migrated.schema.page.widthHundredthMm + 1 && movedBounds.bottom <= migrated.schema.page.heightHundredthMm + 1, "旋转元素多选移动必须限制视觉边界");
}
const rotatedAligned = api.alignSelectedV3Elements(rotatedLayoutState, "center-horizontal");
const rotatedCenters = rotatedLayoutElements.map((element) => {
  const alignedElement = api.findV3Element(rotatedAligned.schema, element.id).element;
  const alignedBounds = api.reportDesignerV3ElementBounds(alignedElement);
  return (alignedBounds.left + alignedBounds.right) / 2;
});
assert(Math.max(...rotatedCenters) - Math.min(...rotatedCenters) <= 1, "旋转元素水平居中对齐必须使用视觉中心线");
const duplicated = api.duplicateSelectedV3Elements({ ...moved, selectedIds: [insertedId] });
assert(duplicated.selectedIds.length === 1 && duplicated.selectedIds[0] !== insertedId, "复制必须生成新 ID 并选中新元素");
assert(!duplicated.schema.layers.some((layer) => layer.id === duplicated.selectedIds[0]), "复制生成的元素 ID 不得与图层 ID 冲突");

const invalidOrientation = api.updateV3Page({ ...state }, { orientation: "Diagonal" });
assert(invalidOrientation.schema.page.orientation === "Landscape" && invalidOrientation.schema.page.widthHundredthMm === 29700 && invalidOrientation.schema.page.heightHundredthMm === 21000, "运行时非法页面方向必须保持当前有效 A4 方向");
const normalizedBodyPrint = api.updateV3Layer(
  invalidOrientation,
  invalidOrientation.schema.layers.find((layer) => layer.role === "Body").id,
  { print: { repeatOnEveryPage: true, keepTogether: true, pinToPageBottom: true, minHeightHundredthMm: 999999 } },
);
const normalizedBodyLayer = normalizedBodyPrint.schema.layers.find((layer) => layer.role === "Body");
assert(normalizedBodyLayer.print.repeatOnEveryPage === false && normalizedBodyLayer.print.pinToPageBottom === false && normalizedBodyLayer.print.minHeightHundredthMm === 26000, "图层打印设置必须按角色和资源上限归一化");

const barrierElements = [
  { ...api.createV3TextElement(1000, 1000), id: "barrier-a", zIndex: 10 },
  { ...api.createV3TextElement(3000, 1000), id: "barrier-locked", zIndex: 20, locked: true },
  { ...api.createV3TextElement(5000, 1000), id: "barrier-b", zIndex: 30 },
  { ...api.createV3TextElement(7000, 1000), id: "barrier-c", zIndex: 40 },
];
const barrierSchema = {
  ...migrated.schema,
  layers: migrated.schema.layers.map((layer, index) => index === 0 ? { ...layer, elements: barrierElements } : { ...layer, elements: [] }),
};
const barrierState = { ...api.createReportDesignerV3DocumentState(barrierSchema), selectedIds: ["barrier-c"] };
const barrierMoved = api.setV3ElementZIndex(barrierState, "barrier-c", "back");
const lockedBarrier = api.findV3Element(barrierMoved.schema, "barrier-locked").element;
assert(lockedBarrier.zIndex === 20, "图层排序不得重编号或移动锁定元素");
assert(api.findV3Element(barrierMoved.schema, "barrier-c").element.zIndex === 30, "锁定元素必须形成 z-order barrier");
assert(api.findV3Element(barrierMoved.schema, "barrier-b").element.zIndex === 40, "同一可编辑区内排序应交换 z-order 槽位");
const frontWithinSegment = api.setV3ElementZIndex({ ...barrierMoved, selectedIds: ["barrier-c"] }, "barrier-c", "front");
assert(api.findV3Element(frontWithinSegment.schema, "barrier-locked").element.zIndex === 20, "置顶操作不得跨越锁定元素");

const layoutElements = [
  api.createV3TextElement(1000, 1800),
  api.createV3TextElement(6500, 4200),
  api.createV3TextElement(14000, 7600),
].map((element, index) => ({
  ...element,
  id: `layout-${index + 1}`,
  widthHundredthMm: 2400 + index * 600,
  heightHundredthMm: 800 + index * 200,
}));
const layoutSchema = {
  ...migrated.schema,
  layers: migrated.schema.layers.map((layer, index) => index === 0
    ? { ...layer, elements: layoutElements }
    : { ...layer, elements: [] }),
};
let layoutState = { ...api.createReportDesignerV3DocumentState(layoutSchema), selectedIds: layoutElements.map((element) => element.id) };
const alignedLeft = api.alignSelectedV3Elements(layoutState, "left");
const alignedLeftXs = layoutElements.map((element) => api.findV3Element(alignedLeft.schema, element.id).element.xHundredthMm);
assert(new Set(alignedLeftXs).size === 1 && alignedLeftXs[0] === 1000, "多选左对齐必须对齐到选区边界");
const alignedMiddle = api.alignSelectedV3Elements(layoutState, "center-vertical");
const middleCenters = layoutElements.map((element) => {
  const found = api.findV3Element(alignedMiddle.schema, element.id).element;
  return found.yHundredthMm + found.heightHundredthMm / 2;
});
assert(new Set(middleCenters).size === 1, "多选垂直居中必须使用同一中心线");
const distributed = api.distributeSelectedV3Elements(layoutState, "horizontal");
const distributedElements = layoutElements.map((element) => api.findV3Element(distributed.schema, element.id).element);
const distributedGaps = distributedElements.slice(1).map((element, index) => element.xHundredthMm - (distributedElements[index].xHundredthMm + distributedElements[index].widthHundredthMm));
assert(Math.abs(distributedGaps[0] - distributedGaps[1]) <= 1, "水平分布必须保持相等边缘间距");
const verticalDistributed = api.distributeSelectedV3Elements(layoutState, "vertical");
const verticalElements = layoutElements.map((element) => api.findV3Element(verticalDistributed.schema, element.id).element);
assert(verticalElements[0].yHundredthMm === 1800 && verticalElements[2].yHundredthMm + verticalElements[2].heightHundredthMm <= 29700, "垂直分布必须保留外边界并限制在页面内");

const exported = api.exportReportDesignerV3SchemaToHtml(migrated.schema);
assert(exported.includes("@page { size: 297mm 210mm"), "V3 导出必须输出横版 A4");
assert(!exported.includes("http://") && !exported.includes("https://"), "V3 导出不得产生外部图片 URL");
const parsedRoundtrip = api.parseReportDesignerV3FromHtml(exported, "ExportDocument");
assert(parsedRoundtrip.schema.page.size === "A4" && parsedRoundtrip.schema.page.orientation === "Landscape", "V3 HTML roundtrip 必须保留 A4 横版");
const inferred = api.parseReportDesignerV3FromHtml("<style>@page { size: A4 landscape; }</style>", "ExportDocument");
assert(inferred.schema.page.orientation === "Landscape", "无 schema 的旧模板应识别 @page 方向并创建 V3 替换草稿");
assert(inferred.sourceVersion === null && inferred.migrated, "无 V3 schema 的旧模板必须只创建一次性 V3 替换草稿");
assert(inferred.issues.some((issue) => issue.message.includes("高级 HTML") && issue.message.includes("确认")), "经典 HTML 必须明确保持高级 HTML，转换需人工确认");

const complexClassic = api.analyzeClassicReportTemplateHtml(`
  <style>.seal { position: absolute; writing-mode: vertical-rl; }</style>
  <table><tr><td colspan="3"><table><tr><td>字段</td></tr></table></td></tr></table>
  {{ for item in items }}{{ if item.Name }}<tr><td>{{ item.Name }}</td></tr>{{ end }}{{ end }}
  <svg><line x1="0" y1="0" x2="1" y2="1" /></svg><img src="{{ seal }}" />
`);
assert(complexClassic.complexity === "complex" && complexClassic.conversion === "classic-only", "嵌套表格、循环和 SVG 经典模板必须标记为 classic-only");
assert(complexClassic.nestedTableCount === 1 && complexClassic.svgCount === 1, "经典模板结构统计必须识别嵌套表格和 SVG");
assert(complexClassic.summary.includes("不能保证原版式等价"), "复杂经典模板必须提示无法保证原版式等价");

for (const classicPath of [
  "Templates/Export/customs_declaration_template.html",
  "Templates/Export/packing_list_template.html",
  "Templates/Export/invoice_template.html",
  "Templates/Export/contract_template.html",
  "Templates/Internal/payment_voucher_template.html",
  "Templates/Internal/expense_reimbursement_template.html",
]) {
  const classic = api.parseReportDesignerV3FromHtml(fs.readFileSync(path.join(repoRoot, classicPath), "utf8"), classicPath.includes("Internal") ? "PaymentVoucher" : "ExportDocument");
  assert(classic.sourceVersion === null && !classic.hadSchema, `${classicPath} 必须保持无 schema 的高级 HTML 模式`);
  assert(classic.migrated && classic.issues.some((issue) => issue.message.includes("高级 HTML")), `${classicPath} 打开时只能提示可选 V3 草稿，不能当作 V3 运行`);
}

const brokenV3 = api.parseReportDesignerV3FromHtml(
  "<!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA { this-is-not-json } -->",
  "ExportDocument",
);
assert(brokenV3.sourceVersion === 3 && brokenV3.migrated, "损坏的 V3 schema 必须保留 sourceVersion=3 并要求确认");
assert(brokenV3.issues.some((issue) => issue.severity === "error"), "损坏的 V3 schema 必须产生阻断错误");

const paymentCrossDomain = {
  ...migrated.schema,
  reportType: "PaymentVoucher",
  layers: migrated.schema.layers.map((layer, index) => index === 0
    ? {
        ...layer,
        elements: [{
          ...layer.elements[0],
          id: "payment-cross-domain",
          type: "Field",
          fieldPath: "Invoice.TotalAmount",
          text: undefined,
        },],
      }
    : { ...layer, elements: [] }),
};
const paymentValidation = api.normalizeReportDesignerV3Schema(paymentCrossDomain, "PaymentVoucher");
assert(paymentValidation.issues.some((issue) => issue.severity === "error" && issue.path.includes("fieldPath")), "付款模板混用 Invoice.* 字段必须阻断");
assert(api.exportReportDesignerV3SchemaToHtml(paymentCrossDomain, "PaymentVoucher") === "", "有字段域阻断错误时不得导出可保存 HTML");
assert(api.validateReportDesignerV3Export(paymentCrossDomain, "PaymentVoucher").blocked, "导出状态必须暴露字段域阻断错误");

const exportCrossDomain = {
  ...migrated.schema,
  reportType: "ExportDocument",
  layers: migrated.schema.layers.map((layer, index) => index === 0
    ? {
        ...layer,
        elements: [{
          ...layer.elements[0],
          id: "export-cross-domain",
          type: "Field",
          fieldPath: "Payment.Amount",
          text: undefined,
        },],
      }
    : { ...layer, elements: [] }),
};
const exportValidation = api.normalizeReportDesignerV3Schema(exportCrossDomain, "ExportDocument");
assert(exportValidation.issues.some((issue) => issue.severity === "error" && issue.path.includes("fieldPath")), "出口模板混用 Payment.* 字段必须阻断");
assert(api.validateReportDesignerV3Export(exportCrossDomain, "ExportDocument").blocked, "出口模板导出状态必须暴露字段域阻断错误");
assert(workspaceSource.includes("当前草稿不能保存"), "V3 工作区必须明确提示阻断草稿不能保存");
assert(workspaceSource.includes("exportValidation.blocked") && workspaceSource.includes("onDesignerDraftContentChange?.(\"\")"), "阻断导出时必须清理陈旧草稿而保留原始内容");

const v3NeedsReview = api.parseReportDesignerV3FromHtml(
  exported.replace('"size": "A4"', '"size": "Letter"'),
  "ExportDocument",
);
assert(v3NeedsReview.sourceVersion === 3 && v3NeedsReview.migrated, "带规范化警告的 V3 模板必须要求显式确认");

const featureBase = {
  xHundredthMm: 1000,
  yHundredthMm: 700,
  widthHundredthMm: 19000,
  heightHundredthMm: 700,
  rotationDeg: 0,
  zIndex: 0,
  visible: true,
  locked: false,
  style: {},
  outputEnabled: true,
};
const featureSchema = {
  version: 3,
  reportType: "ExportDocument",
  page: {
    size: "A4",
    orientation: "Portrait",
    widthHundredthMm: 21000,
    heightHundredthMm: 29700,
    marginTopHundredthMm: 1000,
    marginRightHundredthMm: 1000,
    marginBottomHundredthMm: 1000,
    marginLeftHundredthMm: 1000,
    fontFamily: "Arial, sans-serif",
    fontSizePt: 9,
  },
  grid: { enabled: true, sizeHundredthMm: 500, snap: true },
  layers: [
    {
      id: "feature-header",
      name: "页眉",
      role: "Header",
      print: { repeatOnEveryPage: true, keepTogether: true, pinToPageBottom: false, minHeightHundredthMm: 1200 },
      visible: true,
      locked: false,
      elements: [{ ...featureBase, id: "feature-header-text", type: "Text", text: "HEADER" }],
    },
    {
      id: "feature-body",
      name: "主体",
      role: "Body",
      print: { repeatOnEveryPage: false, keepTogether: false, pinToPageBottom: false, minHeightHundredthMm: 0 },
      visible: true,
      locked: false,
      elements: [{
        ...featureBase,
        id: "feature-page-break",
        yHundredthMm: 4200,
        heightHundredthMm: 500,
        type: "Flow",
        flowKind: "PageBreak",
        block: { id: "feature-page-break-block", type: "PageBreak" },
      }],
    },
    {
      id: "feature-footer",
      name: "页脚",
      role: "Footer",
      print: { repeatOnEveryPage: true, keepTogether: true, pinToPageBottom: true, minHeightHundredthMm: 900 },
      visible: true,
      locked: false,
      elements: [{ ...featureBase, id: "feature-footer-text", yHundredthMm: 28600, heightHundredthMm: 600, type: "Text", text: "FOOTER" }],
    },
    { id: "feature-overlay", name: "覆盖层", role: "Overlay", print: { repeatOnEveryPage: false, keepTogether: false, pinToPageBottom: false, minHeightHundredthMm: 0 }, visible: true, locked: false, elements: [] },
  ],
};
const featureHtml = api.exportReportDesignerV3SchemaToHtml(featureSchema, "ExportDocument");
assert(featureHtml.includes("edm-v3-repeat-layer-header") && featureHtml.includes("edm-v3-repeat-layer-footer"), "重复页眉/页脚必须生成固定重复层");
assert(featureHtml.includes("edm-v3-layer-keep-together"), "保持整段属性必须生成 keep-together 类");
assert(featureHtml.includes("edm-v3-flow-item-pagebreak"), "Flow 页面断点必须保留结构化输出");
assert(featureHtml.includes("top: 291mm"), "贴底页脚必须把内容底边对齐到 A4 物理页底");

const staticHeaderFlowSchema = {
  ...featureSchema,
  layers: featureSchema.layers.map((layer) => layer.role === "Header"
    ? {
        ...layer,
        elements: [{
          ...featureBase,
          id: "static-header-row",
          type: "Flow",
          flowKind: "Row",
          block: { id: "static-header-row-block", type: "Row", columns: [{ id: "header-row-column", contentKind: "Text", text: "页眉固定流", fieldPath: "", widthPercent: 100, style: {} }] },
        }],
      }
    : layer),
};
const staticHeaderFlowHtml = api.exportReportDesignerV3SchemaToHtml(staticHeaderFlowSchema, "ExportDocument");
assert(staticHeaderFlowHtml.includes("edm-v3-flow-static"), "页眉/页脚/覆盖层中的固定 Flow 必须显式标记为静态图层内容");
const invalidOverlayFlowSchema = {
  ...staticHeaderFlowSchema,
  layers: staticHeaderFlowSchema.layers.map((layer) => layer.role === "Overlay"
    ? {
        ...layer,
        elements: [{
          ...featureBase,
          id: "invalid-overlay-pagebreak",
          type: "Flow",
          flowKind: "PageBreak",
          block: { id: "invalid-overlay-pagebreak-block", type: "PageBreak" },
        }],
      }
    : layer),
};
assert(api.validateReportDesignerV3Export(invalidOverlayFlowSchema, "ExportDocument").blocked, "覆盖层中的分页 Flow 必须阻断而不是产生歧义输出");
const overlappingBodySchema = {
  ...featureSchema,
  layers: featureSchema.layers.map((layer) => layer.role === "Body"
    ? {
        ...layer,
        elements: [
          { ...featureBase, id: "overlap-flow", type: "Flow", flowKind: "Row", block: { id: "overlap-flow-block", type: "Row", columns: [{ id: "overlap-column", contentKind: "Text", text: "流", fieldPath: "", widthPercent: 100, style: {} }] } },
          { ...featureBase, id: "overlap-text", type: "Text", text: "静态覆盖", yHundredthMm: 800, heightHundredthMm: 1200 },
        ],
      }
    : layer),
};
assert(api.validateReportDesignerV3Export(overlappingBodySchema, "ExportDocument").issues.some((issue) => issue.message.includes("视觉区域重叠")), "主体静态元素与 Flow 重叠必须给出明确打印提示");

const controlledImageSchema = {
  ...featureSchema,
  layers: featureSchema.layers.map((layer, index) => index === 0
    ? {
        ...layer,
        elements: [{
          ...featureBase,
          id: "controlled-image",
          type: "Image",
          sourceKind: "Resource",
          resourceId: "seal-2024",
          altText: "印章",
          hideWhenSourceEmpty: false,
        }],
      }
    : { ...layer, elements: [] }),
};
const controlledImageHtml = api.exportReportDesignerV3SchemaToHtml(controlledImageSchema, "ExportDocument");
assert(controlledImageHtml.includes("受控资源：seal-2024"), "资源图片必须以受控 resourceId 占位输出");
assert(!controlledImageHtml.includes("src=\"http") && !controlledImageHtml.includes("src=\"https"), "受控图片不得产生任意外部 URL");

for (const unsafeFieldPath of ["Invoice.LogoUrl", "Customer.Logo", "http://evil.example/image.png", "https://evil.example/image.png", "ShowSeal"]) {
  const unsafeFieldSchema = {
    ...controlledImageSchema,
    layers: controlledImageSchema.layers.map((layer, index) => index === 0
      ? { ...layer, elements: [{ ...layer.elements[0], sourceKind: "Field", fieldPath: unsafeFieldPath, resourceId: undefined }] }
      : layer),
  };
  const unsafeFieldValidation = api.normalizeReportDesignerV3Schema(unsafeFieldSchema, "ExportDocument");
  assert(unsafeFieldValidation.issues.some((issue) => issue.severity === "error" && issue.path.includes("fieldPath")), `不受控图片字段 ${unsafeFieldPath} 必须阻断`);
  assert(api.exportReportDesignerV3SchemaToHtml(unsafeFieldSchema, "ExportDocument") === "", `不受控图片字段 ${unsafeFieldPath} 不得导出`);
}
const safeFieldSchema = {
  ...controlledImageSchema,
  layers: controlledImageSchema.layers.map((layer, index) => index === 0
    ? { ...layer, elements: [{ ...layer.elements[0], sourceKind: "Field", fieldPath: "doc_seal_path", resourceId: undefined }] }
    : layer),
};
const safeFieldHtml = api.exportReportDesignerV3SchemaToHtml(safeFieldSchema, "ExportDocument");
assert(safeFieldHtml.includes('src="{{ doc_seal_path }}"'), "受控 data URI 图片字段必须输出字段绑定");

const unsafeLegacyImage = api.migrateReportDesignerSchemaV2ToV3({
  ...legacyA5,
  sections: [{
    ...legacyA5.sections[0],
    blocks: [{ ...legacyA5.sections[0].blocks[0], id: "legacy-image", type: "Image", sourceKind: "Field", fieldPath: "Invoice.LogoUrl", url: "" }],
  }],
});
const migratedImage = unsafeLegacyImage.schema.layers[0].elements[0];
assert(migratedImage.type === "Image" && migratedImage.sourceKind === "Resource" && !migratedImage.resourceId, "不受控旧图片字段必须迁移为安全资源占位");
assert(unsafeLegacyImage.issues.some((issue) => issue.message.includes("受控 data URI")), "不受控旧图片字段迁移必须给出明确提示");

const unsafeImageSchema = {
  ...controlledImageSchema,
  layers: controlledImageSchema.layers.map((layer, index) => index === 0
    ? { ...layer, elements: [{ ...layer.elements[0], resourceId: "https://evil.example/image.png" }] }
    : layer),
};
const unsafeImageHtml = api.exportReportDesignerV3SchemaToHtml(unsafeImageSchema, "ExportDocument");
assert(!unsafeImageHtml.includes("evil.example") && !unsafeImageHtml.includes("https://"), "非法资源标识必须被清理，不能进入导出 HTML");

console.log("report-designer-v3-contract test passed");
