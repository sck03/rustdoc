// Report families match Resources/Fonts/OpenSource/font-manifest.json.
// Sans has real 400/700 faces; Serif has only the bundled 400 face.
export const portableReportSansFontFamily = "Noto Sans CJK SC";

export const portableReportSerifFontFamily = "Noto Serif CJK SC";

export function buildPortableCanvasFont(weight: number, sizePx: number) {
  return `${weight} ${sizePx}px ${portableReportSansFontFamily}`;
}
