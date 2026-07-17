import type { ApiSupplierAssessmentDto } from "../../api/index.ts";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { buildSupplierAssessmentAnalytics } from "./supplierAssessmentAnalyticsModel.ts";

export function SupplierAssessmentAnalytics({ rows, supplierName, onCreate }: {
  rows: readonly ApiSupplierAssessmentDto[];
  supplierName: string;
  onCreate?: () => void;
}) {
  const analytics = buildSupplierAssessmentAnalytics(rows);
  if (!analytics.totalCount) {
    return <div className="empty-cell-content supplier-assessment-analysis-empty">
      <strong>暂无可分析的评价</strong>
      <span>至少记录一次评价后，这里会自动汇总四项得分、结论分布和最近趋势。</span>
      {onCreate ? <button className="primary-button" type="button" onClick={onCreate}>记录第一次评价</button> : null}
    </div>;
  }

  const latest = rows.reduce((current, row) => {
    if (!current) return row;
    const dateDifference = Date.parse(row.assessedAt) - Date.parse(current.assessedAt);
    return dateDifference > 0 || (dateDifference === 0 && row.id > current.id) ? row : current;
  }, null as ApiSupplierAssessmentDto | null);

  return <div className="supplier-assessment-analysis">
    <div className="supplier-assessment-summary-grid">
      <SummaryCard label="最近综合分" value={`${analytics.latestAverage?.toFixed(2) ?? "-"} / 5`}
        detail={formatChange(analytics.changeFromPrevious)} tone="primary" />
      <SummaryCard label="累计评价" value={`${analytics.totalCount} 次`} detail={`最近展示 ${analytics.trend.length} 次趋势`} tone="info" />
      <SummaryCard label="表现优势" value={analytics.strongestDimension?.label ?? "-"}
        detail={`${analytics.strongestDimension?.average.toFixed(2) ?? "-"} / 5`} tone="positive" />
      <SummaryCard label="优先改善" value={analytics.weakestDimension?.label ?? "-"}
        detail={`${analytics.weakestDimension?.average.toFixed(2) ?? "-"} / 5`} tone="warning" />
    </div>

    <div className="supplier-assessment-analysis-grid">
      <section className="supplier-analysis-card">
        <div className="supplier-analysis-card-header"><div><h4>四项平均表现</h4><p>基于 {supplierName} 的全部评价记录</p></div></div>
        <div className="supplier-dimension-list">
          {analytics.dimensions.map((item) => <div className="supplier-dimension-row" key={item.key}>
            <span>{item.label}</span><div className="supplier-score-track" aria-label={`${item.label}平均 ${item.average.toFixed(2)} 分`}>
              <span style={{ width: `${item.average / 5 * 100}%` }} />
            </div><strong>{item.average.toFixed(2)}</strong>
          </div>)}
        </div>
      </section>

      <section className="supplier-analysis-card">
        <div className="supplier-analysis-card-header"><div><h4>评价结论分布</h4><p>用于识别合作风险，不自动修改供应商状态</p></div></div>
        <div className="supplier-conclusion-list">
          {analytics.conclusions.map((item) => <div className="supplier-conclusion-row" key={item.conclusion}>
            <BusinessStatusBadge value={item.conclusion} />
            <div className="supplier-conclusion-track"><span style={{ width: `${item.percentage}%` }} /></div>
            <strong>{item.count}</strong><span>{item.percentage.toFixed(0)}%</span>
          </div>)}
        </div>
      </section>
    </div>

    <section className="supplier-analysis-card supplier-trend-card">
      <div className="supplier-analysis-card-header"><div><h4>最近评分趋势</h4><p>按评价日期展示最近 12 次，纵轴范围 1 至 5 分</p></div>
        {latest ? <div className="supplier-latest-conclusion"><span>最近结论</span><BusinessStatusBadge value={latest.conclusion} /></div> : null}
      </div>
      <TrendChart points={analytics.trend} />
    </section>
  </div>;
}

function SummaryCard({ label, value, detail, tone }: { label: string; value: string; detail: string; tone: string }) {
  return <article className="supplier-assessment-summary" data-tone={tone}>
    <span>{label}</span><strong>{value}</strong><small>{detail}</small>
  </article>;
}

function TrendChart({ points }: { points: ReturnType<typeof buildSupplierAssessmentAnalytics>["trend"] }) {
  if (points.length === 1) return <div className="supplier-single-trend"><strong>{points[0].averageScore.toFixed(2)}</strong><span>{points[0].assessedAt.slice(0, 10)} · 继续评价后可查看变化趋势</span></div>;
  const width = 720;
  const height = 210;
  const paddingX = 34;
  const paddingY = 24;
  const coordinates = points.map((point, index) => ({
    ...point,
    x: paddingX + index * ((width - paddingX * 2) / Math.max(points.length - 1, 1)),
    y: paddingY + (5 - point.averageScore) / 4 * (height - paddingY * 2),
  }));
  const line = coordinates.map((point) => `${point.x},${point.y}`).join(" ");
  return <div className="supplier-trend-chart">
    <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="供应商最近评分趋势图">
      {[1, 2, 3, 4, 5].map((score) => {
        const y = paddingY + (5 - score) / 4 * (height - paddingY * 2);
        return <g key={score}><line className="supplier-trend-grid-line" x1={paddingX} x2={width - paddingX} y1={y} y2={y} /><text x="8" y={y + 4}>{score}</text></g>;
      })}
      <polyline className="supplier-trend-line" points={line} />
      {coordinates.map((point) => <g key={point.id}>
        <circle className="supplier-trend-point" cx={point.x} cy={point.y} r="5"><title>{`${point.assessedAt.slice(0, 10)}：${point.averageScore.toFixed(2)} 分`}</title></circle>
      </g>)}
    </svg>
    <div className="supplier-trend-labels"><span>{points[0].assessedAt.slice(0, 10)}</span><span>{points.at(-1)?.assessedAt.slice(0, 10)}</span></div>
  </div>;
}

function formatChange(value: number | null) {
  if (value === null) return "首次评价，暂无环比";
  if (value === 0) return "较上次持平";
  return `较上次 ${value > 0 ? "+" : ""}${value.toFixed(2)}`;
}
