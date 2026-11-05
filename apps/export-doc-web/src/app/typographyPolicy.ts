// Interface and report families match Resources/Fonts/OpenSource/font-manifest.json.
// Sans has real 400/700 faces; Serif has only the bundled 400 face.
export const portableReportSansFontFamily = "Noto Sans CJK SC";

export const portableReportSerifFontFamily = "Noto Serif CJK SC";

export const portableReportFontFaces = [
  { value: "sans", label: "黑体 · 常规（Noto Sans）", fontFamily: portableReportSansFontFamily, bold: false },
  { value: "sans-bold", label: "黑体 · 粗体（Noto Sans）", fontFamily: portableReportSansFontFamily, bold: true },
  { value: "serif", label: "宋体 · 常规（Noto Serif）", fontFamily: portableReportSerifFontFamily, bold: false },
] as const;

export function installPortableFonts() {
  const sources = [
    [portableReportSansFontFamily, new URL("../../../../Resources/Fonts/OpenSource/NotoSansCJKsc-Regular.otf", import.meta.url).href, "400"],
    [portableReportSansFontFamily, new URL("../../../../Resources/Fonts/OpenSource/NotoSansCJKsc-Bold.otf", import.meta.url).href, "700"],
    [portableReportSerifFontFamily, new URL("../../../../Resources/Fonts/OpenSource/NotoSerifCJKsc-Regular.otf", import.meta.url).href, "400"],
  ] as const;
  for (const [family, source, weight] of sources) {
    document.fonts.add(new FontFace(family, `url("${source}")`, { weight, style: "normal", display: "swap" }));
  }
}

export function sharePortableFonts(target: FontFaceSet) {
  for (const face of document.fonts) {
    const family = face.family.replace(/^["']|["']$/g, "");
    if (family === portableReportSansFontFamily || family === portableReportSerifFontFamily) target.add(face);
  }
}

export function buildPortableCanvasFont(weight: number, sizePx: number) {
  return `${weight} ${sizePx}px ${portableReportSansFontFamily}`;
}
