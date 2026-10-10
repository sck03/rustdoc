import { portableReportFontFaces, portableReportSansFontFamily, portableReportSerifFontFamily } from "../../app/typographyPolicy.ts";
import { DesignerCheckbox as CheckRow } from "./ReportDesignerPropertyControls.tsx";
import { ReportDesignerV3ColorField } from "./ReportDesignerV3ColorField.tsx";
import { NumberField, SelectField } from "./ReportDesignerV3InspectorControls.tsx";
import { hundredthMmToMm, REPORT_DESIGNER_V3_DEFAULT_TEXT_COLOR, type ReportDesignerV3Element, type ReportDesignerV3ElementStyle } from "./reportDesignerV3Schema.ts";

const fontFamilies = portableReportFontFaces.filter(face => !face.bold).map(face => ({ value: face.fontFamily, label: face.label }));
const inheritedFont = { value: "", label: "沿用页面字体" };

export function ReportDesignerFontFamilyField({ label, value, disabled, allowInherit = false, onChange }: {
  label: string; value: string; disabled: boolean; allowInherit?: boolean; onChange: (value: string) => void;
}) {
  return <SelectField label={label} value={value} options={allowInherit ? [inheritedFont, ...fontFamilies] : fontFamilies} disabled={disabled} onChange={onChange} />;
}

export function FlowStyleEditor({ style, editable, onPatch }: { style: ReportDesignerV3ElementStyle; editable: boolean; onPatch: (update: Partial<ReportDesignerV3ElementStyle>) => void }) {
  return <details className="report-designer-property-section"><summary>字体与颜色</summary>
    <ReportDesignerFontFamilyField label="表格字体" value={style.fontFamily ?? ""} allowInherit disabled={!editable} onChange={fontFamily => onPatch({ fontFamily: fontFamily || undefined })} />
    <ReportDesignerV3ColorField label="表格文字颜色" value={style.color ?? REPORT_DESIGNER_V3_DEFAULT_TEXT_COLOR} disabled={!editable} onCommit={color => onPatch({ color })} />
  </details>;
}

export function ElementStyleEditor({ element, pageFontSizePt, editable, onPatch }: { element: ReportDesignerV3Element; pageFontSizePt: number; editable: boolean; onPatch: (update: Partial<ReportDesignerV3ElementStyle>) => void }) {
  const style = element.style;
  const line = element.type === "Line";
  const text = element.type === "Text" || element.type === "Field" || element.type === "PageNumber";
  const font = style.bold ? "sans-bold" : style.fontFamily === portableReportSerifFontFamily ? "serif" : style.fontFamily ? "sans" : "";
  return <div className="report-designer-v3-style-editor">
    {text && <>
      <SelectField label="字体" value={font} options={[inheritedFont, ...portableReportFontFaces]} disabled={!editable} onChange={value => {
        const face = portableReportFontFaces.find(face => face.value === value);
        onPatch({ fontFamily: face?.fontFamily, bold: face?.bold });
      }} />
      <strong>文字</strong><div className="report-designer-v3-inspector-grid">
        <NumberField label="字号 pt" value={style.fontSizePt ?? pageFontSizePt} min={6} max={96} disabled={!editable} onCommit={(fontSizePt) => onPatch({ fontSizePt })} />
        <SelectField label="对齐" value={style.align ?? "Left"} options={[{ value: "Left", label: "左" }, { value: "Center", label: "中" }, { value: "Right", label: "右" }]} disabled={!editable} onChange={(align) => onPatch({ align: align as "Left" | "Center" | "Right" })} />
        <SelectField label="垂直对齐" value={style.verticalAlign ?? "Top"} options={[{ value: "Top", label: "顶部" }, { value: "Middle", label: "居中" }, { value: "Bottom", label: "底部" }]} disabled={!editable} onChange={verticalAlign => onPatch({ verticalAlign: verticalAlign as "Top" | "Middle" | "Bottom" })} />
      </div>
      <CheckRow checked={style.bold === true} disabled={!editable} onChange={(bold) => onPatch({ bold, ...(bold ? { fontFamily: portableReportSansFontFamily } : {}) })}>粗体</CheckRow>
    </>}
    <details className="report-designer-property-section" open={element.type !== "Field"}><summary>颜色与边框</summary>
    {text && <ReportDesignerV3ColorField label="文字颜色" value={style.color ?? REPORT_DESIGNER_V3_DEFAULT_TEXT_COLOR} disabled={!editable} onCommit={(color) => onPatch({ color })} />}
    {!line && <ReportDesignerV3ColorField label="背景颜色" value={style.backgroundColor ?? ""} allowEmpty disabled={!editable} onCommit={(backgroundColor) => onPatch({ backgroundColor: backgroundColor || undefined })} />}
    <strong>{line ? "线条" : "边框"}</strong>
    <div className="report-designer-v3-inspector-grid">
      <SelectField label={line ? "线型" : "边框样式"} value={style.borderStyle ?? (style.borderWidthPx ? "Solid" : "None")} options={[{ value: "None", label: "无" }, { value: "Solid", label: "实线" }, { value: "Dashed", label: "虚线" }]} disabled={!editable}
        onChange={(value) => onPatch({ borderStyle: value as "None" | "Solid" | "Dashed", borderWidthPx: value === "None" ? style.borderWidthPx : (style.borderWidthPx ?? 0) > 0 ? style.borderWidthPx : 1 })} />
      <NumberField label="线宽 px" value={style.borderWidthPx ?? (line ? 1 : 0)} min={0} max={8} disabled={!editable} onCommit={(borderWidthPx) => onPatch({ borderWidthPx })} />
    </div>
    <ReportDesignerV3ColorField label={line ? "线条颜色" : "边框颜色"} value={style.borderColor ?? "#334155"} disabled={!editable} onCommit={(borderColor) => onPatch({ borderColor })} />
    {(text || element.type === "Image") && <NumberField label="内边距 (mm)" value={hundredthMmToMm(style.paddingHundredthMm ?? 0)} min={0} max={20} disabled={!editable} onCommit={(value) => onPatch({ paddingHundredthMm: Math.round(value * 100) })} />}
    </details>
  </div>;
}
