// Report families match Resources/Fonts/OpenSource/font-manifest.json.
// Sans has real 400/700 faces; Serif has only the bundled 400 face.
export const portableReportSansFontFamily = "Noto Sans CJK SC";

export const portableReportSerifFontFamily = "Noto Serif CJK SC";

export const portableReportFontFaces = [
  { value: "sans", label: "黑体 · 常规（Noto Sans）", fontFamily: portableReportSansFontFamily, bold: false },
  { value: "sans-bold", label: "黑体 · 粗体（Noto Sans）", fontFamily: portableReportSansFontFamily, bold: true },
  { value: "serif", label: "宋体 · 常规（Noto Serif）", fontFamily: portableReportSerifFontFamily, bold: false },
] as const;

export function buildPortableCanvasFont(weight: number, sizePx: number) {
  return `${weight} ${sizePx}px ${portableReportSansFontFamily}`;
}
