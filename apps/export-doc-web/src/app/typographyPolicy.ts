export const portableReportSansFontFamily = [
  '"Noto Sans CJK SC"',
  '"Noto Sans SC"',
  '"Source Han Sans SC"',
  '"PingFang SC"',
  '"Microsoft YaHei UI"',
  '"Microsoft YaHei"',
  '"Segoe UI"',
  "Arial",
  "sans-serif",
].join(", ");

export const portableReportSerifFontFamily = [
  '"Noto Serif CJK SC"',
  '"Noto Serif SC"',
  '"Source Han Serif SC"',
  '"Songti SC"',
  "SimSun",
  '"Times New Roman"',
  "serif",
].join(", ");

export function buildPortableCanvasFont(weight: number, sizePx: number) {
  return `${weight} ${sizePx}px ${portableReportSansFontFamily}`;
}
