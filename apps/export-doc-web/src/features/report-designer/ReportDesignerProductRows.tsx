import type { ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";

/** Edit one product row, as in a banded report designer; printing repeats it. */
export function ReportDesignerProductRows({ schema }: { schema: ReportDesignerV3Schema }) {
  const fields = schema.layers.filter(layer => layer.visible && layer.role === "Body").flatMap(layer => layer.elements).filter(element => element.visible && element.type === "Field" && element.fieldPath.startsWith("item."));
  if (!fields.length) return null;
  const top = Math.min(...fields.map(field => field.yHundredthMm));
  const bottom = Math.max(...fields.map(field => field.yHundredthMm + field.heightHundredthMm));
  return <div className="report-designer-product-guides" aria-hidden="true">
    <div className="report-designer-product-row-label" style={{ top: `${top / 100}mm` }}>商品明细 · 只设计一行，打印时逐件重复</div>
    <div className="report-designer-product-row-end" style={{ top: `${bottom / 100}mm` }} />
  </div>;
}
