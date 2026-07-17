export type SupplierAssessmentAnalyticsInput = {
  id: number;
  assessedAt: string;
  qualityScore: number;
  deliveryScore: number;
  serviceScore: number;
  priceScore: number;
  averageScore: number;
  conclusion: string;
};

export type SupplierAssessmentDimension = {
  key: "quality" | "delivery" | "service" | "price";
  label: string;
  average: number;
};

export type SupplierAssessmentConclusionDistribution = {
  conclusion: string;
  count: number;
  percentage: number;
};

export type SupplierAssessmentTrendPoint = {
  id: number;
  assessedAt: string;
  averageScore: number;
};

export type SupplierAssessmentAnalytics = {
  totalCount: number;
  latestAverage: number | null;
  changeFromPrevious: number | null;
  dimensions: SupplierAssessmentDimension[];
  strongestDimension: SupplierAssessmentDimension | null;
  weakestDimension: SupplierAssessmentDimension | null;
  conclusions: SupplierAssessmentConclusionDistribution[];
  trend: SupplierAssessmentTrendPoint[];
};

const CONCLUSIONS = ["优先合作", "合格", "观察", "暂停合作"] as const;

export function buildSupplierAssessmentAnalytics(
  rows: readonly SupplierAssessmentAnalyticsInput[],
): SupplierAssessmentAnalytics {
  const ordered = [...rows].sort((left, right) => {
    const dateDifference = Date.parse(right.assessedAt) - Date.parse(left.assessedAt);
    return dateDifference || right.id - left.id;
  });
  const totalCount = ordered.length;
  const dimensions = totalCount ? [
    dimension("quality", "质量", ordered.reduce((sum, row) => sum + row.qualityScore, 0) / totalCount),
    dimension("delivery", "交期", ordered.reduce((sum, row) => sum + row.deliveryScore, 0) / totalCount),
    dimension("service", "服务", ordered.reduce((sum, row) => sum + row.serviceScore, 0) / totalCount),
    dimension("price", "价格", ordered.reduce((sum, row) => sum + row.priceScore, 0) / totalCount),
  ] : [];
  const rankedDimensions = [...dimensions].sort((left, right) => right.average - left.average);
  const conclusionCounts = new Map<string, number>();
  for (const row of ordered) {
    const conclusion = row.conclusion.trim() || "未设置";
    conclusionCounts.set(conclusion, (conclusionCounts.get(conclusion) ?? 0) + 1);
  }
  const extraConclusions = [...conclusionCounts.keys()].filter((item) => !CONCLUSIONS.includes(item as typeof CONCLUSIONS[number]));
  const conclusions = [...CONCLUSIONS, ...extraConclusions].map((conclusion) => {
    const count = conclusionCounts.get(conclusion) ?? 0;
    return { conclusion, count, percentage: totalCount ? round(count / totalCount * 100) : 0 };
  }).filter((item) => item.count > 0);

  return {
    totalCount,
    latestAverage: ordered[0]?.averageScore ?? null,
    changeFromPrevious: ordered.length > 1 ? round(ordered[0].averageScore - ordered[1].averageScore) : null,
    dimensions,
    strongestDimension: rankedDimensions[0] ?? null,
    weakestDimension: rankedDimensions.at(-1) ?? null,
    conclusions,
    trend: ordered.slice(0, 12).reverse().map((row) => ({
      id: row.id,
      assessedAt: row.assessedAt,
      averageScore: round(row.averageScore),
    })),
  };
}

function dimension(key: SupplierAssessmentDimension["key"], label: string, average: number): SupplierAssessmentDimension {
  return { key, label, average: round(average) };
}

function round(value: number) {
  return Math.round(value * 100) / 100;
}
