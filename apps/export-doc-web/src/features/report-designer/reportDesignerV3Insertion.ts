import type { ReportDesignerV3DocumentState } from "./reportDesignerV3Mutations.ts";
import { resolveV3InsertionLayer } from "./reportDesignerV3Regions.ts";
import {
  REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER, REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS,
  type ReportDesignerV3Element,
} from "./reportDesignerV3Schema.ts";

export function getV3ProductLayoutIssue(elements: Iterable<ReportDesignerV3Element>) {
  let productFields = false, detailTable = false;
  for (const element of elements) {
    productFields ||= element.type === "Field" && element.fieldPath.startsWith("item.");
    detailTable ||= element.type === "Flow" && element.flowKind === "DetailTable";
    if (productFields && detailTable) return "自由商品字段与高级明细表不能混用，请选择一种商品排版方式。";
  }
  return null;
}

/** Resolve the actual target before checking capacity; product fields belong to the body. */
export function getV3InsertionIssue(state: ReportDesignerV3DocumentState, layerId: string | null, element: ReportDesignerV3Element) {
  if (!layerId) return "请先选择可编辑的图层。";
  const layer = resolveV3InsertionLayer(state.schema, layerId, element);
  if (!layer) return "当前图层或所需主体区域已锁定、隐藏，请先在图层面板调整。";
  if (layer.elements.length >= REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER) return `该图层最多 ${REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER} 个元素。`;
  const elements = state.schema.layers.flatMap(candidate => candidate.elements);
  if (elements.length >= REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS) return `模板最多 ${REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS} 个元素。`;
  return getV3ProductLayoutIssue([...elements, element]);
}
