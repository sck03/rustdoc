import { useQuery } from "@tanstack/react-query";
import {
  ArrowDownRight,
  ArrowRight,
  ArrowUpRight,
  BadgeCheck,
  BookOpen,
  CalendarDays,
  ClipboardList,
  FilePlus2,
  ListChecks,
  Minus,
  RefreshCw,
  Search,
  Ship,
  TrendingUp,
  WalletCards,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";

export function DashboardPage({ client }: { client: ExportDocManagerApiClient }) {
  const navigate = useNavigate();
  const invoicePermission = useModulePermission("document.invoices");
  const queryPermission = useModulePermission("document.query");
  const masterDataPermission = useModulePermission("document.master-data");
  const hsKnowledgePermission = useModulePermission("document.hs-knowledge");
  const jobsPermission = useModulePermission("document.jobs");
  const dashboardQuery = useQuery({
    queryKey: queryKeys.dashboard(),
    queryFn: ({ signal }) => client.getDashboard({ signal }),
  });

  const dashboard = dashboardQuery.data;
  const recentInvoices = dashboard?.recentInvoices ?? [];
  const todoItems = dashboard?.todoItems ?? [];
  const metrics = [
    {
      label: "本月出口额",
      value: dashboard ? formatAmount(dashboard.monthlyExportAmount) : "—",
      detail: dashboard ? buildMonthTrend(dashboard.monthlyExportAmount, dashboard.previousMonthlyExportAmount) : null,
      icon: CalendarDays,
      tone: "teal",
      route: "/query/invoices",
      enabled: queryPermission.canView,
    },
    {
      label: "本月预估利润",
      value: dashboard ? formatAmount(dashboard.monthlyProfit) : "—",
      detail: dashboard ? buildMonthTrend(dashboard.monthlyProfit, dashboard.previousMonthlyProfit) : null,
      icon: TrendingUp,
      tone: "green",
      route: "/query/invoices",
      enabled: queryPermission.canView,
    },
    {
      label: "本月退税额",
      value: dashboard ? formatAmount(dashboard.monthlyTaxRefund) : "—",
      detail: dashboard ? buildMonthTrend(dashboard.monthlyTaxRefund, dashboard.previousMonthlyTaxRefund) : null,
      icon: WalletCards,
      tone: "violet",
      route: "/query/invoices",
      enabled: queryPermission.canView,
    },
    {
      label: "待处理订单",
      value: dashboard ? formatCount(dashboard.pendingCount) : "—",
      detail: dashboard ? { text: `草稿 ${dashboard.draftCount} · 已核对 ${dashboard.verifiedCount}`, direction: "flat" as const } : null,
      icon: ClipboardList,
      tone: "amber",
      route: "/invoices",
      enabled: invoicePermission.canView,
    },
    {
      label: "已出运",
      value: dashboard ? formatCount(dashboard.shippedCount) : "—",
      detail: dashboard ? { text: `已结汇 ${dashboard.completedCount}`, direction: "flat" as const } : null,
      icon: Ship,
      tone: "blue",
      route: "/invoices",
      enabled: invoicePermission.canView,
    },
    {
      label: dashboard?.periodLabel ? `${dashboard.periodLabel}发票` : "本月发票",
      value: dashboard ? formatCount(dashboard.monthlyInvoiceCount) : "—",
      detail: dashboard ? { text: `有效订单共 ${dashboard.totalActiveCount}`, direction: "flat" as const } : null,
      icon: BadgeCheck,
      tone: "slate",
      route: "/invoices",
      enabled: invoicePermission.canView,
    },
  ];
  const quickActions = [
    { label: "新建发票", description: "录入贸易与商品明细", icon: FilePlus2, route: "/invoices/new", enabled: invoicePermission.canOperate },
    { label: "单据查询", description: "检索并导出业务数据", icon: Search, route: "/query/invoices", enabled: queryPermission.canView },
    { label: "HS 查询", description: "查税则与申报经验", icon: BookOpen, route: "/master-data/hs-knowledge/search", enabled: hsKnowledgePermission.canView },
    { label: "任务中心", description: "跟踪后台处理进度", icon: ListChecks, route: "/jobs", enabled: jobsPermission.canView },
  ].filter((action) => action.enabled);

  const isBusy = dashboardQuery.isFetching;
  const errorMessage = dashboardQuery.isError ? readApiError(dashboardQuery.error) : null;
  const showFirstUseGuide = Boolean(dashboard && dashboard.totalActiveCount === 0 && !errorMessage);

  function openInvoice(referenceId: string | number) {
    const invoiceId = Number(referenceId);
    if (Number.isInteger(invoiceId) && invoiceId > 0) {
      navigate(`/invoices/${invoiceId}`);
    }
  }

  return (
    <section className="dashboard-page" aria-label="仪表盘">
      <div className="toolbar dashboard-toolbar">
        <div className="toolbar-summary">
          <strong>{dashboard?.singleWindowStatusSummary ?? "单一窗口近况：加载中。"}</strong>
          {dashboard?.periodLabel ? <span>统计周期：{dashboard.periodLabel}</span> : null}
        </div>
        <div className="toolbar-actions">
          <button
            className="command-button secondary"
            type="button"
            onClick={() => void dashboardQuery.refetch()}
            disabled={isBusy}
            title="刷新仪表盘"
          >
            <RefreshCw size={16} aria-hidden="true" />
            <span>{isBusy ? "刷新中" : "刷新"}</span>
          </button>
        </div>
      </div>

      {errorMessage ? <InlineNotice tone="error" title="仪表盘数据加载失败">{errorMessage}</InlineNotice> : null}
      {dashboardQuery.isLoading ? <PageState tone="loading" title="正在加载仪表盘" description="正在读取订单、金额和待办摘要。" /> : null}

      {quickActions.length > 0 ? (
        <section className="dashboard-quick-section" aria-label="快捷操作">
          <div className="dashboard-section-heading">
            <div>
              <h2>快捷操作</h2>
              <span>常用业务入口</span>
            </div>
          </div>
          <div className="dashboard-quick-grid">
            {quickActions.map((action) => {
              const Icon = action.icon;
              return (
                <button className="dashboard-quick-action" type="button" key={action.route} onClick={() => navigate(action.route)}>
                  <span className="dashboard-quick-icon"><Icon size={18} aria-hidden="true" /></span>
                  <span>
                    <strong>{action.label}</strong>
                    <small>{action.description}</small>
                  </span>
                  <ArrowRight size={16} aria-hidden="true" />
                </button>
              );
            })}
          </div>
        </section>
      ) : null}

      {showFirstUseGuide ? (
        <section className="dashboard-first-use" aria-label="首次使用向导">
          <div>
            <span className="dashboard-first-use-kicker">首次使用</span>
            <h2>从基础资料开始建立第一笔业务</h2>
            <p>建议先维护客户、出口商与商品资料，再创建发票并完成 HS 归类和单据输出。</p>
          </div>
          <ol>
            <li><strong>1</strong><span>维护基础资料</span></li>
            <li><strong>2</strong><span>新建出口发票</span></li>
            <li><strong>3</strong><span>核对并输出单据</span></li>
          </ol>
          {masterDataPermission.canView ? (
            <button className="command-button" type="button" onClick={() => navigate("/master-data")}>开始配置</button>
          ) : null}
        </section>
      ) : null}

      <div className="dashboard-metric-grid">
        {metrics.map((metric) => {
          const Icon = metric.icon;
          const TrendIcon = metric.detail?.direction === "up"
            ? ArrowUpRight
            : metric.detail?.direction === "down"
              ? ArrowDownRight
              : Minus;
          return (
            <button
              className={`dashboard-metric dashboard-metric-${metric.tone}`}
              type="button"
              key={metric.label}
              disabled={!metric.enabled}
              onClick={() => navigate(metric.route)}
              aria-label={`${metric.label}：${metric.value}`}
            >
              <span className="dashboard-metric-icon"><Icon size={19} aria-hidden="true" /></span>
              <span className="dashboard-metric-content">
                <span className="dashboard-metric-label">{metric.label}</span>
                <strong>{metric.value}</strong>
                {metric.detail ? (
                  <small className={`dashboard-metric-trend dashboard-trend-${metric.detail.direction}`}>
                    <TrendIcon size={13} aria-hidden="true" />
                    {metric.detail.text}
                  </small>
                ) : null}
              </span>
            </button>
          );
        })}
      </div>

      <div className="dashboard-work-grid">
        <section className="form-section dashboard-recent-section" aria-label="最新订单">
          <div className="section-header dashboard-section-header">
            <div><h2>最新订单</h2><span>最近更新的有效发票</span></div>
            {invoicePermission.canView ? (
              <button className="command-button secondary compact-command-button" type="button" onClick={() => navigate("/invoices")}>查看全部</button>
            ) : null}
          </div>
          <ResponsiveTableFrame className="dashboard-table-frame" label="最新订单" mobileLayout="cards" busy={isBusy}>
            <table className="dashboard-recent-table">
              <thead>
                <tr>
                  <th>发票号</th>
                  <th>状态</th>
                  <th>客户</th>
                  <th>日期</th>
                  <th className="amount-cell">金额</th>
                </tr>
              </thead>
              <tbody>
                {recentInvoices.length > 0 ? (
                  recentInvoices.map((invoice) => (
                    <tr
                      className="clickable-row"
                      key={invoice.id}
                      tabIndex={0}
                      onClick={() => openInvoice(invoice.id)}
                      onKeyDown={(event) => {
                        if (event.key === "Enter" || event.key === " ") {
                          event.preventDefault();
                          openInvoice(invoice.id);
                        }
                      }}
                    >
                      <td className="strong-cell" data-label="发票号">{invoice.invoiceNo}</td>
                      <td data-label="状态"><span className="status-pill">{invoice.statusText || invoice.status}</span></td>
                      <td data-label="客户">{invoice.customerNameEN || "-"}</td>
                      <td data-label="日期" data-table-priority="secondary">{formatDate(invoice.invoiceDate)}</td>
                      <td className="amount-cell" data-label="金额">{formatAmount(invoice.totalAmount)}</td>
                    </tr>
                  ))
                ) : !dashboardQuery.isLoading && !errorMessage ? (
                  <tr><td className="empty-cell" colSpan={5}>{isBusy ? "加载中" : "暂无订单"}</td></tr>
                ) : null}
              </tbody>
            </table>
          </ResponsiveTableFrame>
        </section>

        <section className="form-section dashboard-todo-section" aria-label="待办事项">
          <div className="section-header dashboard-section-header">
            <div><h2>待办事项</h2><span>优先处理的业务节点</span></div>
            {invoicePermission.canView ? (
              <button className="command-button secondary compact-command-button" type="button" onClick={() => navigate("/invoices")}>查看全部</button>
            ) : null}
          </div>
          <div className="dashboard-todo-list">
            {todoItems.length > 0 ? (
              todoItems.map((item, index) => (
                <button
                  className="dashboard-todo-item"
                  key={`${item.actionType}-${item.referenceId}-${index}`}
                  type="button"
                  onClick={() => openInvoice(item.referenceId)}
                >
                  <span>{item.title}</span>
                  <strong>{item.description}</strong>
                  <ArrowUpRight size={16} aria-hidden="true" />
                </button>
              ))
            ) : !dashboardQuery.isLoading && !errorMessage ? (
              <div className="small-empty">{isBusy ? "加载中" : "当前没有待办事项"}</div>
            ) : null}
          </div>
        </section>
      </div>
    </section>
  );
}

function buildMonthTrend(current: number, previous: number) {
  if (previous === 0) {
    return current === 0
      ? { text: "与上月持平", direction: "flat" as const }
      : { text: "较上月新增", direction: "up" as const };
  }

  const percent = ((current - previous) / Math.abs(previous)) * 100;
  if (Math.abs(percent) < 0.05) {
    return { text: "与上月持平", direction: "flat" as const };
  }

  return {
    text: `较上月 ${Math.abs(percent).toLocaleString("zh-CN", { maximumFractionDigits: 1 })}%`,
    direction: percent > 0 ? "up" as const : "down" as const,
  };
}

function formatAmount(value: number | undefined) {
  return (value ?? 0).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatCount(value: number | undefined) {
  return String(value ?? 0);
}

function formatDate(value: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString("zh-CN");
}
