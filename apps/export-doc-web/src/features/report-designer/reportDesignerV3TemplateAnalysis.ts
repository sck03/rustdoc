/**
 * Conservative, dependency-free analysis for classic HTML templates.
 *
 * The V3 canvas can create a useful re-layout draft, but it cannot promise
 * pixel-equivalent conversion of arbitrary HTML/CSS.  Keeping this analysis
 * separate from migration makes that distinction explicit and testable.
 */
export type ReportDesignerV3ClassicTemplateComplexity = "empty" | "simple" | "structured" | "complex";
export type ReportDesignerV3ClassicTemplateConversion = "safe" | "review" | "classic-only";

export type ReportDesignerV3ClassicTemplateAnalysis = {
  complexity: ReportDesignerV3ClassicTemplateComplexity;
  conversion: ReportDesignerV3ClassicTemplateConversion;
  score: number;
  tableCount: number;
  nestedTableCount: number;
  rowCount: number;
  mergedCellCount: number;
  flowConstructCount: number;
  svgCount: number;
  imageCount: number;
  positionedElementCount: number;
  hasPageBreaks: boolean;
  hasScript: boolean;
  signals: string[];
  summary: string;
};

const tagPattern = /<\/?table\b[^>]*>/gi;
const mergedCellPattern = /<(?:td|th)\b[^>]*(?:colspan|rowspan)\s*=\s*["']?\s*(\d+)/gi;
const flowPattern = /\{\{\s*(?:for|if|case|capture|assign|while)\b/gi;

export function analyzeClassicReportTemplateHtml(content: string): ReportDesignerV3ClassicTemplateAnalysis {
  const source = typeof content === "string" ? content : "";
  if (!source.trim()) {
    return createAnalysis("empty", "safe", 0, [], {
      tableCount: 0,
      nestedTableCount: 0,
      rowCount: 0,
      mergedCellCount: 0,
      flowConstructCount: 0,
      svgCount: 0,
      imageCount: 0,
      positionedElementCount: 0,
      hasPageBreaks: false,
      hasScript: false,
    });
  }

  const tableCount = count(source, /<table\b/gi);
  const nestedTableCount = countNestedTables(source);
  const rowCount = count(source, /<tr\b/gi);
  const mergedCellCount = countMergedCells(source);
  const flowConstructCount = count(source, flowPattern);
  const svgCount = count(source, /<svg\b/gi);
  const imageCount = count(source, /<img\b/gi);
  const positionedElementCount = countPositioningSignals(source);
  const hasPageBreaks = /(?:break-before|page-break-before|page-break-after|page-break-inside|class\s*=\s*["'][^"']*page-break)/i.test(source);
  const hasScript = /<script\b|\bon[a-z]+\s*=/i.test(source);

  let score = 0;
  const signals: string[] = [];
  addSignal(tableCount > 1, 2, "包含多个表格区块", signals, (value) => { score += value; });
  addSignal(nestedTableCount > 0, 3, "包含嵌套表格", signals, (value) => { score += value; });
  addSignal(mergedCellCount >= 4, 2, "包含大量合并单元格", signals, (value) => { score += value; });
  addSignal(flowConstructCount >= 4, 2, "包含循环或条件输出", signals, (value) => { score += value; });
  addSignal(svgCount > 0, 3, "包含 SVG/对角线等矢量绘制", signals, (value) => { score += value; });
  addSignal(positionedElementCount > 0, 2, "包含绝对定位、缩放或竖排布局", signals, (value) => { score += value; });
  addSignal(imageCount > 0, 1, "包含图片或印章资源", signals, (value) => { score += value; });
  addSignal(rowCount > 20, 2, "包含大量明细行", signals, (value) => { score += value; });
  addSignal(hasPageBreaks, 2, "包含显式分页规则", signals, (value) => { score += value; });
  addSignal(hasScript, 5, "包含脚本或事件处理器", signals, (value) => { score += value; });

  const complexity = hasScript || score >= 7
    ? "complex"
    : score >= 3
      ? "structured"
      : "simple";
  const conversion = complexity === "complex"
    ? "classic-only"
    : complexity === "structured"
      ? "review"
      : "safe";
  const summary = conversion === "classic-only"
    ? "该经典 HTML 依赖复杂表格、条件/循环或定位绘制，V3 只能生成可编辑的重排草稿，不能保证原版式等价；建议继续使用经典渲染或高级 HTML。"
    : conversion === "review"
      ? "该经典 HTML 可以生成 V3 重排草稿，但合并单元格、分页和资源位置需要人工复核。"
      : "该经典 HTML 结构较简单，可以生成 V3 草稿；保存前仍应核对预览。";

  return createAnalysis(complexity, conversion, score, signals, {
    tableCount,
    nestedTableCount,
    rowCount,
    mergedCellCount,
    flowConstructCount,
    svgCount,
    imageCount,
    positionedElementCount,
    hasPageBreaks,
    hasScript,
  }, summary);
}

function createAnalysis(
  complexity: ReportDesignerV3ClassicTemplateComplexity,
  conversion: ReportDesignerV3ClassicTemplateConversion,
  score: number,
  signals: string[],
  counts: Omit<ReportDesignerV3ClassicTemplateAnalysis, "complexity" | "conversion" | "score" | "signals" | "summary">,
  summary = "当前模板为空，将从默认 A4 画布开始。",
): ReportDesignerV3ClassicTemplateAnalysis {
  return { complexity, conversion, score, signals, summary, ...counts };
}

function count(value: string, pattern: RegExp) {
  return value.match(pattern)?.length ?? 0;
}

function countMergedCells(value: string) {
  let total = 0;
  for (const match of value.matchAll(mergedCellPattern)) {
    if (Number.parseInt(match[1] ?? "0", 10) > 1) total += 1;
  }
  return total;
}

function countNestedTables(value: string) {
  let depth = 0;
  let nested = 0;
  for (const match of value.matchAll(tagPattern)) {
    if (match[0].startsWith("</")) {
      depth = Math.max(0, depth - 1);
    } else {
      if (depth > 0) nested += 1;
      depth += 1;
    }
  }
  return nested;
}

function countPositioningSignals(value: string) {
  return count(value, /(?:position\s*:\s*(?:absolute|fixed|sticky)|writing-mode\s*:|(?:^|[;\s])zoom\s*:|transform\s*:\s*(?:scale|rotate|translate)|:nth-child\s*\()/gi);
}

function addSignal(
  condition: boolean,
  weight: number,
  signal: string,
  signals: string[],
  addScore: (weight: number) => void,
) {
  if (!condition) return;
  signals.push(signal);
  addScore(weight);
}
