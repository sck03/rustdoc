import { createV3FieldElement, findV3Element, insertV3Element, updateV3Element, type ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import { resolveReportDesignerLayerBands } from "./reportDesignerLayerBands.ts";

/** Click picks an existing column, or fills a free space in the authored product row. */
export function insertProductField(state: ReportDesignerV3DocumentState, field: { label: string; value: string }) {
  const body = state.schema.layers.find(layer => layer.role === "Body" && layer.visible && !layer.locked);
  const active = state.schema.layers.find(layer => layer.id === state.activeLayerId);
  if (!body || !active || active.locked || !active.visible) return { state, notice: "请先选择可编辑的图层。" };
  const fields = body.elements.filter(element => element.visible && element.type === "Field" && element.fieldPath.startsWith("item."));
  const existing = fields.find(element => element.type === "Field" && element.fieldPath === field.value);
  if (existing) return { state: { ...state, selectedIds: [existing.id], activeLayerId: body.id }, notice: null };
  const bands = resolveReportDesignerLayerBands(state.schema);
  const element = { ...createV3FieldElement(field.value), label: field.label };
  const top = fields.length ? Math.min(...fields.map(e => e.yHundredthMm)) : Math.max(bands.headerHeight, 3000);
  const left = fields.length ? Math.min(...fields.map(e => e.xHundredthMm)) : state.schema.page.marginLeftHundredthMm;
  const right = state.schema.page.widthHundredthMm - state.schema.page.marginRightHundredthMm;
  const occupied = body.elements.filter(e => e.visible && e.type !== "Line" && e.type !== "Rectangle" && e.yHundredthMm < top + element.heightHundredthMm && e.yHundredthMm + e.heightHundredthMm > top).sort((a,b) => a.xHundredthMm - b.xHundredthMm);
  let x = left;
  for (const e of occupied) {
    if (e.xHundredthMm + e.widthHundredthMm <= x) continue;
    if (x + element.widthHundredthMm + 100 <= e.xHundredthMm) break;
    x = e.xHundredthMm + e.widthHundredthMm + 100;
  }
  if (x + element.widthHundredthMm > right || top + element.heightHundredthMm > bands.pageHeight - bands.footerHeight) return { state, notice: "当前商品行空间不足。可先缩窄已有字段，或把新字段直接拖到需要的位置。" };
  const next = insertV3Element(state, body.id, { ...element, xHundredthMm: x, yHundredthMm: top });
  return { state: next, notice: next === state ? "当前图层无法添加字段。" : null };
}

const totals: Record<string, string> = { "item.Cartons": "total_by_ctn_unit", "item.Quantity": "total_by_qty_unit", "item.TotalPrice": "Invoice.TotalAmount" };

/** A deliberate one-click alignment; totals remain ordinary, individually editable fields. */
export function alignProductSummary(state: ReportDesignerV3DocumentState, selectedId: string) {
  const selected = findV3Element(state.schema, selectedId)?.element;
  if (selected?.type !== "Field") return state;
  const productPath = Object.keys(totals).find(path => path === selected.fieldPath || totals[path] === selected.fieldPath);
  if (!productPath) return state;
  const elements = state.schema.layers.filter(layer => layer.visible).flatMap(layer => layer.elements).filter(e => e.visible && e.outputEnabled);
  const products = elements.filter(e => e.type === "Field" && e.fieldPath === productPath);
  const summaries = elements.filter(e => e.type === "Field" && e.fieldPath === totals[productPath]);
  if (products.length !== 1 || summaries.length !== 1) return state;
  const source = products[0], target = summaries[0];
  const width = Math.max(source.widthHundredthMm, target.widthHundredthMm);
  const x = source.style.align === "Right" ? source.xHundredthMm + source.widthHundredthMm - width : source.xHundredthMm;
  return updateV3Element(state, target.id, { xHundredthMm: x, widthHundredthMm: width, style: { ...target.style, align: source.style.align } });
}
