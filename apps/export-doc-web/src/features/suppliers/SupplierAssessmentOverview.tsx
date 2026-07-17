import { useEffect, useMemo, useState } from "react";
import type { ApiSupplierAssessmentOverviewDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, errorFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { readApiError } from "../../ui/formUtils.ts";

type OverviewFilter = "all" | "attention" | "preferred";

export function SupplierAssessmentOverview({ client, onOpenSupplier }: {
  client: ExportDocManagerApiClient;
  onOpenSupplier: (supplierId: number) => void;
}) {
  const [overview, setOverview] = useState<ApiSupplierAssessmentOverviewDto | null>(null);
  const [filter, setFilter] = useState<OverviewFilter>("all");
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);

  useEffect(() => {
    setFeedback(null);
    void client.getSupplierAssessmentOverview().then(setOverview)
      .catch((error) => setFeedback(errorFeedback(readApiError(error))));
  }, [client]);

  const rows = useMemo(() => (overview?.items ?? []).filter((item) => {
    if (filter === "attention") return item.conclusion === "观察" || item.conclusion === "暂停合作";
    if (filter === "preferred") return item.conclusion === "优先合作";
    return true;
  }), [filter, overview]);

  if (!overview) return <section className="form-section supplier-procurement-overview">
    <div className="section-header"><div><h3>采购概览</h3><p className="section-description">汇总每家供应商的最新一次人工评价，不生成自动排名。</p></div></div>
    <OperationFeedback feedback={feedback} />
    {!feedback ? <p className="muted-text">正在读取供应商评价...</p> : null}
  </section>;

  if (!overview.totalSuppliers) return <section className="form-section supplier-procurement-overview">
    <div className="section-header"><div><h3>采购概览</h3><p className="section-description">先建立供应商资料，再逐步沉淀可复核的合作评价。</p></div></div>
    <div className="empty-cell-content supplier-assessment-analysis-empty"><strong>尚未建立供应商</strong><span>采购概览不会生成演示数据或虚构评分。</span></div>
  </section>;

  const coverage = Math.round(overview.assessedSuppliers / overview.totalSuppliers * 100);
  return <section className="form-section supplier-procurement-overview">
    <div className="section-header"><div><h3>采购概览</h3><p className="section-description">只使用每家供应商最新一次人工评价，帮助发现待关注对象；综合分不代表自动采购排名。</p></div><span>{overview.assessedSuppliers} / {overview.totalSuppliers} 家已评价</span></div>
    <OperationFeedback feedback={feedback} />
    <div className="supplier-assessment-summary-grid">
      <Summary label="评价覆盖" value={`${coverage}%`} detail={`${overview.unassessedSuppliers} 家尚无评价`} tone="info" />
      <Summary label="优先合作" value={String(overview.preferredCount)} detail="以最新人工结论计" tone="positive" />
      <Summary label="需要关注" value={String(overview.watchCount + overview.pausedCount)} detail={`观察 ${overview.watchCount} · 暂停 ${overview.pausedCount}`} tone="warning" />
      <Summary label="当前合格" value={String(overview.qualifiedCount)} detail="以最新人工结论计" />
    </div>
    <div className="supplier-analysis-card">
      <div className="supplier-analysis-card-header"><div><h4>最新评价维度均分</h4><p>每家供应商只取最新一次评价，避免评价次数多的供应商放大权重。</p></div><span>{overview.assessedSuppliers ? "满分 5 分" : "暂无评分"}</span></div>
      <div className="supplier-overview-dimensions">
        <Dimension label="质量" value={overview.averageQualityScore} />
        <Dimension label="交期" value={overview.averageDeliveryScore} />
        <Dimension label="服务" value={overview.averageServiceScore} />
        <Dimension label="价格" value={overview.averagePriceScore} />
      </div>
    </div>
    <div className="section-header-actions supplier-overview-filter" role="group" aria-label="采购概览筛选">
      <button className={filter === "all" ? "primary-button" : "secondary-button"} type="button" onClick={() => setFilter("all")}>全部已评价</button>
      <button className={filter === "attention" ? "primary-button" : "secondary-button"} type="button" onClick={() => setFilter("attention")}>需要关注</button>
      <button className={filter === "preferred" ? "primary-button" : "secondary-button"} type="button" onClick={() => setFilter("preferred")}>优先合作</button>
    </div>
    <div className="table-frame"><table className="data-table responsive-data-table"><thead><tr>
      <th>供应商</th><th>最新评价</th><th>综合分</th><th data-table-priority="secondary">质量 / 交期 / 服务 / 价格</th><th>结论</th><th />
    </tr></thead><tbody>
      {rows.map((item) => <tr key={item.supplierCompanyId}>
        <td><TablePrimaryText value={item.supplierName} secondary={[item.category, item.supplierStatus].filter(Boolean).join(" · ")} /></td>
        <td><TablePrimaryText value={item.latestAssessedAt.slice(0, 10)} secondary={`${item.latestAssessmentKind} · 共 ${item.assessmentCount} 次`} /></td>
        <td><strong>{item.averageScore.toFixed(2)}</strong> / 5</td>
        <td data-table-priority="secondary">{item.qualityScore} / {item.deliveryScore} / {item.serviceScore} / {item.priceScore}</td>
        <td><BusinessStatusBadge value={item.conclusion} /></td>
        <td><button className="secondary-button" type="button" onClick={() => onOpenSupplier(item.supplierCompanyId)}>查看评价</button></td>
      </tr>)}
      {!rows.length ? <tr><td className="empty-cell" colSpan={6}><div className="empty-cell-content"><strong>当前筛选没有供应商</strong><span>切换筛选查看其它最新评价结论。</span></div></td></tr> : null}
    </tbody></table></div>
    {overview.unassessedSuppliers ? <p className="section-description">另有 {overview.unassessedSuppliers} 家供应商尚无评价，未参与均分和结论统计。</p> : null}
  </section>;
}

function Summary({ label, value, detail, tone }: { label: string; value: string; detail: string; tone?: string }) {
  return <div className="supplier-assessment-summary" data-tone={tone}><span>{label}</span><strong>{value}</strong><small>{detail}</small></div>;
}

function Dimension({ label, value }: { label: string; value: number }) {
  return <div className="supplier-dimension-row"><span>{label}</span><div className="supplier-score-track"><span style={{ width: `${Math.max(0, Math.min(100, value / 5 * 100))}%` }} /></div><strong>{value.toFixed(2)}</strong></div>;
}
