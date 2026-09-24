import type { ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";
import { reportDesignerCanvasElementStyle, ReportDesignerCanvasElementPreview } from "./ReportDesignerCanvasElement.tsx";

/** Guides only: copies are never selectable or saved as extra fields. */
export function ReportDesignerProductRows({ schema }: { schema: ReportDesignerV3Schema }) {
  const fields = schema.layers.filter(layer => layer.visible && layer.role === "Body").flatMap(layer => layer.elements).filter(element => element.visible && element.type === "Field" && element.fieldPath.startsWith("item."));
  if (!fields.length) return null;
  const top = Math.min(...fields.map(field => field.yHundredthMm));
  const bottom = Math.max(...fields.map(field => field.yHundredthMm + field.heightHundredthMm));
  const step = Math.max(schema.detailRowHeightHundredthMm ?? 1200, bottom - top + 100);
  return <div className="report-designer-product-guides" aria-hidden="true">
    <div className="report-designer-product-row-label" style={{ top: `${top / 100}mm` }}>第一件商品 · 拖动字段调整列位</div>
    {[1, 2].filter(index => bottom + step * index < schema.page.heightHundredthMm - schema.page.marginBottomHundredthMm).map(index => <div key={index}>
      <div className="report-designer-product-row-label is-copy" style={{ top: `${(top + step * index) / 100}mm` }}>第 {index + 1} 件 · 自动输出</div>
      {fields.map(field => <div className="report-designer-product-copy" key={field.id} style={reportDesignerCanvasElementStyle({ ...field, yHundredthMm: field.yHundredthMm + step * index })}><ReportDesignerCanvasElementPreview element={field} /></div>)}
    </div>)}
  </div>;
}
