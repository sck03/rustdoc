import { useQuery } from "@tanstack/react-query";
import type { KeyboardEvent } from "react";
import { AlarmClock, ArrowRight, CalendarRange, CircleDollarSign, ContactRound, RefreshCw, TrendingUp, UsersRound } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";

export function SalesDashboardPage({ client }: { client: ExportDocManagerApiClient }) {
  const navigate = useNavigate();
  const query = useQuery({ queryKey: queryKeys.crmDashboard(), queryFn: () => client.getCrmDashboard() });
  const dashboard = query.data;
  const openOpportunityCount = (dashboard?.opportunityStages ?? [])
    .filter((item) => item.stage !== "已成交" && item.stage !== "已失单")
    .reduce((total, item) => total + item.count, 0);
  const quotedOpportunityCount = (dashboard?.opportunityStages ?? [])
    .filter((item) => item.stage === "已报价" || item.stage === "谈判中")
    .reduce((total, item) => total + item.count, 0);
  const metrics = [
    { label: "销售客户", value: dashboard?.customerCount ?? 0, icon: ContactRound, tone: "teal", to: "/crm/follow-ups?view=directory" },
    { label: "联系人", value: dashboard?.contactCount ?? 0, icon: UsersRound, tone: "blue", to: "/crm/follow-ups?view=profile" },
    { label: "待跟进", value: dashboard?.pendingFollowUpCount ?? 0, icon: CalendarRange, tone: "amber", to: "/crm/follow-ups?view=followups" },
    { label: "已逾期", value: dashboard?.overdueFollowUpCount ?? 0, icon: AlarmClock, tone: "violet", to: "/crm/follow-ups?view=followups" },
    { label: "进行中商机", value: openOpportunityCount, icon: TrendingUp, tone: "teal", to: "/crm/opportunities?view=directory" },
    { label: "报价/谈判", value: quotedOpportunityCount, icon: CircleDollarSign, tone: "amber", to: "/crm/opportunities?view=directory" },
  ];
  const isFirstRun = Boolean(dashboard)
    && dashboard!.customerCount === 0
    && dashboard!.contactCount === 0
    && dashboard!.pendingFollowUpCount === 0
    && openOpportunityCount === 0;

  return (
    <section className="dashboard-page" aria-label="销售概览">
      <div className="toolbar">
        <div className="toolbar-summary"><strong>未来七天待跟进：{dashboard?.dueNextSevenDaysCount ?? 0} 项</strong></div>
        <div className="toolbar-actions">
          <button className="command-button primary" type="button" onClick={() => navigate("/crm/follow-ups")}>进入客户跟进</button>
          <button className="command-button secondary" type="button" onClick={() => navigate("/crm/opportunities")}>查看商机</button>
          <button className="command-button secondary" type="button" disabled={query.isFetching} onClick={() => void query.refetch()}>
            <RefreshCw size={16} aria-hidden="true" />{query.isFetching ? "刷新中" : "刷新"}
          </button>
        </div>
      </div>

      {query.isError ? <InlineNotice tone="error" title="销售工作台加载失败">{readApiError(query.error)}</InlineNotice> : null}
      {isFirstRun ? <section className="sales-first-run" aria-label="销售工作区开始使用">
        <div className="sales-first-run-heading">
          <div><span>首次使用</span><h2>从一位客户开始</h2><p>只需完成下面三步，销售概览就会自动形成，不需要先配置复杂流程。</p></div>
        </div>
        <div className="sales-first-run-steps">
          <button type="button" onClick={() => navigate("/crm/follow-ups?view=profile")}>
            <strong><span>1</span>建立客户资料</strong><small>填写公司和主要联系人</small><ArrowRight size={17} aria-hidden="true" />
          </button>
          <button type="button" onClick={() => navigate("/crm/follow-ups?view=followups")}>
            <strong><span>2</span>记录首次跟进</strong><small>留下沟通摘要和下次动作</small><ArrowRight size={17} aria-hidden="true" />
          </button>
          <button type="button" onClick={() => navigate("/crm/opportunities?view=editor")}>
            <strong><span>3</span>建立销售商机</strong><small>跟踪阶段、金额和预计日期</small><ArrowRight size={17} aria-hidden="true" />
          </button>
        </div>
      </section> : null}
      <div className="dashboard-metric-grid">
        {metrics.map((metric) => {
          const Icon = metric.icon;
          return <button type="button" className={`dashboard-metric dashboard-metric-${metric.tone}`} key={metric.label}
            aria-label={`${metric.label} ${metric.value}，打开详情`} onClick={() => navigate(metric.to)}>
            <div className="dashboard-metric-icon"><Icon size={19} aria-hidden="true" /></div>
            <div><span>{metric.label}</span><strong>{metric.value}</strong></div>
          </button>;
        })}
      </div>

      <section className="form-section" aria-label="近期跟进待办">
        <div className="section-header"><h2>近期跟进待办</h2></div>
        <ResponsiveTableFrame label="近期跟进待办" mobileLayout="scroll" busy={query.isFetching}>
          <table className="dashboard-recent-table responsive-data-table">
            <thead><tr><th>客户</th><th data-table-priority="secondary">联系人</th><th>下次动作</th><th>提醒时间</th></tr></thead>
            <tbody>
              {(dashboard?.upcomingFollowUps ?? []).map((item) => <tr className="clickable-row" key={item.id} tabIndex={0} onClick={() => navigate("/crm/follow-ups")} onKeyDown={(event) => handleRowKeyDown(event, () => navigate("/crm/follow-ups"))}>
                <td className="strong-cell">{item.customerName}</td>
                <td data-table-priority="secondary">{item.contactName || "-"}</td>
                <td>{item.nextAction || item.summary}</td>
                <td>{formatDateTime(item.nextFollowUpAt)}</td>
              </tr>)}
              {!query.isFetching && (dashboard?.upcomingFollowUps.length ?? 0) === 0 ? <tr><td className="empty-cell" colSpan={4}>暂无待跟进事项</td></tr> : null}
            </tbody>
          </table>
        </ResponsiveTableFrame>
      </section>

      <div className="two-column-layout">
        <section className="form-section" aria-label="商机阶段漏斗">
          <div className="section-header"><h2>商机阶段漏斗</h2></div>
          <ResponsiveTableFrame label="商机阶段漏斗" mobileLayout="scroll"><table className="data-table responsive-data-table"><thead><tr><th>阶段</th><th>数量</th></tr></thead><tbody>
            {(dashboard?.opportunityStages ?? []).map((item) => <tr className="clickable-row" key={item.stage} tabIndex={0} onClick={() => navigate("/crm/opportunities")} onKeyDown={(event) => handleRowKeyDown(event, () => navigate("/crm/opportunities"))}><td><BusinessStatusBadge value={item.stage} /></td><td>{item.count}</td></tr>)}
            {!dashboard?.opportunityStages.length ? <tr><td className="empty-cell" colSpan={2}>暂无商机阶段数据。</td></tr> : null}
          </tbody></table></ResponsiveTableFrame>
        </section>
        <section className="form-section" aria-label="商机金额汇总">
          <div className="section-header"><h2>进行中商机金额</h2><span>按币种分组</span></div>
          <ResponsiveTableFrame label="进行中商机金额" mobileLayout="scroll"><table className="data-table responsive-data-table"><thead><tr><th>币种</th><th>商机数</th><th>预计金额</th><th>加权金额</th></tr></thead><tbody>
            {(dashboard?.opportunityCurrencies ?? []).map((item) => <tr key={item.currency}><td>{item.currency}</td><td>{item.count}</td><td>{formatAmount(item.estimatedAmount)}</td><td>{formatAmount(item.weightedAmount)}</td></tr>)}
            {!dashboard?.opportunityCurrencies.length ? <tr><td className="empty-cell" colSpan={4}>暂无进行中商机金额。</td></tr> : null}
          </tbody></table></ResponsiveTableFrame>
        </section>
      </div>

      <section className="form-section" aria-label="近期预计成交">
        <div className="section-header"><h2>未来 30 天预计成交</h2></div>
        <ResponsiveTableFrame label="未来 30 天预计成交" mobileLayout="scroll"><table className="data-table responsive-data-table"><thead><tr><th>商机</th><th>客户</th><th data-table-priority="secondary">阶段</th><th>预计金额</th><th data-table-priority="secondary">概率</th><th>预计日期</th></tr></thead><tbody>
          {(dashboard?.upcomingOpportunityClosings ?? []).map((item) => <tr className="clickable-row" key={item.id} tabIndex={0} onClick={() => navigate("/crm/opportunities")} onKeyDown={(event) => handleRowKeyDown(event, () => navigate("/crm/opportunities"))}><td><TablePrimaryText value={item.title} /></td><td><TablePrimaryText value={item.customerName} /></td><td data-table-priority="secondary"><BusinessStatusBadge value={item.stage} /></td><td>{item.currency} {formatAmount(item.estimatedAmount)}</td><td data-table-priority="secondary">{item.probabilityPercent}%</td><td>{formatDate(item.expectedCloseAt)}</td></tr>)}
          {!dashboard?.upcomingOpportunityClosings.length ? <tr><td className="empty-cell" colSpan={6}>未来 30 天暂无预计成交商机。</td></tr> : null}
        </tbody></table></ResponsiveTableFrame>
      </section>
    </section>
  );
}

function formatAmount(value?: number) { return (value ?? 0).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
function formatDate(value?: string) { return value ? new Date(value).toLocaleDateString("zh-CN") : "未设置"; }

function formatDateTime(value?: string) {
  if (!value) return "未设置";
  return new Date(value).toLocaleString("zh-CN", { hour12: false });
}

function handleRowKeyDown(event: KeyboardEvent<HTMLTableRowElement>, action: () => void) {
  if (event.key !== "Enter" && event.key !== " ") return;
  event.preventDefault();
  action();
}
