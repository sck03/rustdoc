import { FormEvent, KeyboardEvent, useEffect, useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Plus, RefreshCw, Search, X } from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { ApiPaymentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission, usePermission } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { formatAmount, formatDate, readApiError, readRouteSuccessMessage } from "../../ui/formUtils.ts";
import { listPageSizeOptions, loadListViewState, normalizeListPageSize, saveListViewState } from "../../ui/listViewState.ts";

const paymentListViewStateStorageKey = "export-doc-manager.payment-list-view-state.v1";

export function PaymentListPage({ client }: { client: ExportDocManagerApiClient }) {
  const paymentPermission = useModulePermission("document.payments");
  const expensePermission = useModulePermission("office.expenses");
  const [initialListViewState] = useState(() => loadListViewState(paymentListViewStateStorageKey));
  const [keyword, setKeyword] = useState(initialListViewState.keyword);
  const [committedKeyword, setCommittedKeyword] = useState(initialListViewState.keyword);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialListViewState.pageSize);
  const navigate = useNavigate();
  const location = useLocation();
  const successMessage = readRouteSuccessMessage(location.state);

  const paymentsQuery = useQuery({
    queryKey: queryKeys.payments(pageNumber, pageSize, committedKeyword.trim()),
    queryFn: ({ signal }) =>
      client.listPayments({
        pageNumber,
        pageSize,
        keyword: committedKeyword.trim() || undefined,
      }, { signal }),
    placeholderData: keepPreviousData,
  });

  useServerPageNumber(paymentsQuery, pageNumber, setPageNumber);

  useEffect(() => {
    saveListViewState(paymentListViewStateStorageKey, {
      keyword: committedKeyword,
      pageSize,
    });
  }, [committedKeyword, pageSize]);

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const nextKeyword = keyword.trim();
    setKeyword(nextKeyword);
    setCommittedKeyword(nextKeyword);
    setPageNumber(1);
  }

  function handleResetSearch() {
    setKeyword("");
    setCommittedKeyword("");
    setPageNumber(1);
  }

  function handlePageSizeChange(value: number) {
    setPageSize(normalizeListPageSize(value));
    setPageNumber(1);
  }

  const payments = paymentsQuery.data ?? null;
  const message = paymentsQuery.isError ? readApiError(paymentsQuery.error) : null;
  const isBusy = paymentsQuery.isFetching;

  return (
    <section className="work-surface" aria-label="付款报销列表">
      <div className="toolbar">
        <form className="search-form" onSubmit={handleSearch}>
          <Search size={17} aria-hidden="true" />
          <input
            aria-label="搜索付款报销"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="付款单号、参考号、收款方、付款方、部门、项目"
          />
        </form>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="重置搜索" aria-label="重置搜索"
            disabled={isBusy || (!keyword && !committedKeyword)}
            onClick={handleResetSearch}
          >
            <X size={18} aria-hidden="true" />
          </button>
          <button
            className="icon-button"
            type="button"
            title="刷新" aria-label="刷新"
            disabled={isBusy}
            onClick={() => void paymentsQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          {paymentPermission.canOperate ? (
            <button className="command-button" type="button" onClick={() => navigate("/payments/new")}>
              <Plus size={17} aria-hidden="true" />
              <span>新建</span>
            </button>
          ) : null}
        </div>
      </div>

      <InlineNotice tone="info" action={expensePermission.canView ? <Link to="/office/requests/expense">报销申请与审批</Link> : undefined}>
        填写付款单或报销打印单，保存后选择模板打印或导出 PDF。打印单不代表审批通过或已经付款。
      </InlineNotice>
      {message ? <InlineNotice tone="error" title="付款记录加载失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}

      <PaymentTable
        data={payments?.items ?? []}
        isBusy={isBusy}
        hasError={Boolean(paymentsQuery.isError)}
        onOpen={(paymentId) => navigate(`/payments/${paymentId}`)}
      />

      <ListPaginationControls
        pageNumber={payments?.pageNumber ?? pageNumber}
        totalPages={Math.max(payments?.totalPages ?? 1, 1)}
        totalCount={payments?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
    </section>
  );
}

function PaymentTable({
  data,
  isBusy,
  hasError,
  onOpen,
}: {
  data: ApiPaymentDto[];
  isBusy: boolean;
  hasError: boolean;
  onOpen: (paymentId: number) => void;
}) {
  function handleRowKeyDown(event: KeyboardEvent<HTMLTableRowElement>, paymentId: number) {
    if (event.target !== event.currentTarget) return;
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      onOpen(paymentId);
    }
  }

  return (
    <ResponsiveTableFrame label="付款报销列表" busy={isBusy} mobileLayout="scroll">
      <table className="payment-table">
        <thead>
          <tr>
            <th>付款单号</th>
            <th>发票号／业务参考号</th>
            <th>付款日期</th>
            <th>收款方</th>
            <th>付款方</th>
            <th>部门</th>
            <th>项目</th>
            <th>品名</th>
            <th className="amount-cell">USD</th>
            <th className="amount-cell">CNY</th>
            <th>方式</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 && !hasError ? (
            <tr>
              <td colSpan={12} className="empty-cell">
                {isBusy ? "加载中" : "暂无数据"}
              </td>
            </tr>
          ) : (
            data.map((payment) => (
              <tr
                className="clickable-row"
                key={payment.id}
                tabIndex={0}
                onClick={() => onOpen(payment.id)}
                onKeyDown={(event) => handleRowKeyDown(event, payment.id)}
              >
                <td className="strong-cell">{payment.voucherNo || "-"}</td>
                <td>{payment.invoiceNo || "-"}</td>
                <td>{formatDate(payment.paymentDate)}</td>
                <td>{payment.payeeName || "-"}</td>
                <td>{payment.payerName || "-"}</td>
                <td>{payment.department || "-"}</td>
                <td>{payment.project || "-"}</td>
                <td>{payment.goodsName || "-"}</td>
                <td className="amount-cell">{formatAmount(payment.usdAmount, "USD")}</td>
                <td className="amount-cell">{formatAmount(payment.cnyAmount, "CNY")}</td>
                <td>
                  <span className="status-pill">{payment.paymentMethod || "-"}</span>
                </td>
                <td><PaymentOutputLink payment={payment} /></td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </ResponsiveTableFrame>
  );
}

function PaymentOutputLink({ payment }: { payment: ApiPaymentDto }) {
  const preview = usePermission(permissionResources.paymentOutput, permissionActions.preview, payment);
  const print = usePermission(permissionResources.paymentOutput, permissionActions.print, payment);
  const pdf = usePermission(permissionResources.paymentOutput, permissionActions.exportPdf, payment);
  if (!preview.allowed && !print.allowed && !pdf.allowed) return null;
  return <Link to={`/payments/${payment.id}?section=report`} onClick={(event) => event.stopPropagation()}>打印/PDF</Link>;
}
