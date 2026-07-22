import { useQuery } from "@tanstack/react-query";
import {
  ArrowUpRight,
  BadgeCheck,
  CalendarDays,
  ClipboardList,
  RefreshCw,
  Ship,
  TrendingUp,
  WalletCards,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

export function DashboardPage({ client }: { client: ExportDocManagerApiClient }) {
  const navigate = useNavigate();
  const dashboardQuery = useQuery({
    queryKey: queryKeys.dashboard(),
    queryFn: () => client.getDashboard(),
  });

  const dashboard = dashboardQuery.data;
  const recentInvoices = dashboard?.recentInvoices ?? [];
  const todoItems = dashboard?.todoItems ?? [];
  const metrics = [
    {
      label: "本月出口额",
      value: formatAmount(dashboard?.monthlyExportAmount),
      icon: CalendarDays,
      tone: "teal",
    },
    {
      label: "本月预估利润",
      value: formatAmount(dashboard?.monthlyProfit),
      icon: TrendingUp,
      tone: "green",
    },
    {
      label: "本月退税额",
      value: formatAmount(dashboard?.monthlyTaxRefund),
      icon: WalletCards,
      tone: "violet",
    },
    {
      label: "待处理订单",
      value: formatCount(dashboard?.pendingCount),
      icon: ClipboardList,
      tone: "amber",
    },
    {
      label: "已出运",
      value: formatCount(dashboard?.shippedCount),
      icon: Ship,
      tone: "blue",
    },
    {
      label: "总订单量",
      value: formatCount(dashboard?.totalActiveCount),
      icon: BadgeCheck,
      tone: "slate",
    },
  ];

  const isBusy = dashboardQuery.isFetching;
  const errorMessage = dashboardQuery.isError ? readApiError(dashboardQuery.error) : null;

  function openInvoice(referenceId: string | number) {
    const invoiceId = Number(referenceId);
    if (Number.isInteger(invoiceId) && invoiceId > 0) {
      navigate(`/invoices/${invoiceId}`);
    }
  }

  return (
    <section className="dashboard-page" aria-label="仪表盘">
      <div className="toolbar">
        <div className="toolbar-summary">
          <strong>{dashboard?.singleWindowStatusSummary ?? "单一窗口近况：加载中。"}</strong>
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

      {errorMessage ? <div className="alert">{errorMessage}</div> : null}

      <div className="dashboard-metric-grid">
        {metrics.map((metric) => {
          const Icon = metric.icon;
          return (
            <div className={`dashboard-metric dashboard-metric-${metric.tone}`} key={metric.label}>
              <div className="dashboard-metric-icon">
                <Icon size={19} aria-hidden="true" />
              </div>
              <div>
                <span>{metric.label}</span>
                <strong>{metric.value}</strong>
              </div>
            </div>
          );
        })}
      </div>

      <div className="dashboard-work-grid">
        <section className="form-section dashboard-recent-section" aria-label="最新订单">
          <div className="section-header">
            <h2>最新订单</h2>
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
                        if (event.key === "Enter") {
                          openInvoice(invoice.id);
                        }
                      }}
                    >
                      <td className="strong-cell" data-label="发票号">{invoice.invoiceNo}</td>
                      <td data-label="状态">
                        <span className="status-pill">{invoice.statusText || invoice.status}</span>
                      </td>
                      <td data-label="客户">{invoice.customerNameEN || "-"}</td>
                      <td data-label="日期" data-table-priority="secondary">{formatDate(invoice.invoiceDate)}</td>
                      <td className="amount-cell" data-label="金额">{formatAmount(invoice.totalAmount)}</td>
                    </tr>
                  ))
                ) : (
                  <tr>
                    <td className="empty-cell" colSpan={5}>
                      {isBusy ? "加载中" : "暂无订单"}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </ResponsiveTableFrame>
        </section>

        <section className="form-section dashboard-todo-section" aria-label="待办事项">
          <div className="section-header">
            <h2>待办事项</h2>
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
            ) : (
              <div className="small-empty">{isBusy ? "加载中" : "暂无待办"}</div>
            )}
          </div>
        </section>
      </div>
    </section>
  );
}

function formatAmount(value: number | undefined) {
  return (value ?? 0).toLocaleString("zh-CN", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

function formatCount(value: number | undefined) {
  return String(value ?? 0);
}

function formatDate(value: string) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleDateString("zh-CN");
}
