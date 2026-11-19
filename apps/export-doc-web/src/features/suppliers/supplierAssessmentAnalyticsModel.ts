import type { ApiSupplierAssessmentAnalyticsDto } from "../../api/index.ts";

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
  assessmentDate: string;
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
  summary: ApiSupplierAssessmentAnalyticsDto,
): SupplierAssessmentAnalytics {
  const ordered = [...summary.recent].sort((left, right) => {
    const dateDifference = right.assessmentDate.localeCompare(left.assessmentDate);
    return dateDifference || right.id - left.id;
  });
  const totalCount = summary.totalCount;
  const dimensions = totalCount ? [
    dimension("quality", "质量", summary.qualityTotal / totalCount),
    dimension("delivery", "交期", summary.deliveryTotal / totalCount),
    dimension("service", "服务", summary.serviceTotal / totalCount),
    dimension("price", "价格", summary.priceTotal / totalCount),
  ] : [];
  const rankedDimensions = [...dimensions].sort((left, right) => right.average - left.average);
  const counts = [summary.preferredCount, summary.qualifiedCount, summary.watchCount, summary.pausedCount];
  const conclusions = CONCLUSIONS.map((conclusion, index) => {
    const count = counts[index];
    return { conclusion, count, percentage: totalCount ? round(count / totalCount * 100) : 0 };
  }).filter(item => item.count > 0);

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
      assessmentDate: row.assessmentDate,
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
