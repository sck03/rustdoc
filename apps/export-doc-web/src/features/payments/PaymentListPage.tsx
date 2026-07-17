import { FormEvent, KeyboardEvent, useEffect, useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Plus, RefreshCw, Search, X } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import { ApiPaymentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { formatAmount, formatDate, readApiError, readRouteSuccessMessage } from "../../ui/formUtils.ts";
import { listPageSizeOptions, loadListViewState, normalizeListPageSize, saveListViewState } from "../../ui/listViewState.ts";

const paymentListViewStateStorageKey = "export-doc-manager.payment-list-view-state.v1";

export function PaymentListPage({ client }: { client: ExportDocManagerApiClient }) {
  const paymentPermission = useModulePermission("document.payments");
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
    queryFn: () =>
      client.listPayments({
        pageNumber,
        pageSize,
        keyword: committedKeyword.trim() || undefined,
      }),
    placeholderData: keepPreviousData,
  });

  useEffect(() => {
    if (paymentsQuery.data && paymentsQuery.data.pageNumber !== pageNumber) {
      setPageNumber(paymentsQuery.data.pageNumber);
    }
  }, [paymentsQuery.data, pageNumber]);

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
            placeholder="发票号、收款方、付款方、部门、项目"
          />
        </form>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="重置搜索"
            disabled={isBusy || (!keyword && !committedKeyword)}
            onClick={handleResetSearch}
          >
            <X size={18} aria-hidden="true" />
          </button>
          <button
            className="icon-button"
            type="button"
            title="刷新"
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

      {message ? <div className="alert">{message}</div> : null}
      {successMessage ? <div className="success-alert">{successMessage}</div> : null}

      <PaymentTable
        data={payments?.items ?? []}
        isBusy={isBusy}
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
  onOpen,
}: {
  data: ApiPaymentDto[];
  isBusy: boolean;
  onOpen: (paymentId: number) => void;
}) {
  function handleRowKeyDown(event: KeyboardEvent<HTMLTableRowElement>, paymentId: number) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      onOpen(paymentId);
    }
  }

  return (
    <div className="table-frame" aria-busy={isBusy}>
      <table className="payment-table">
        <thead>
          <tr>
            <th>发票号</th>
            <th>付款日期</th>
            <th>收款方</th>
            <th>付款方</th>
            <th>部门</th>
            <th>项目</th>
            <th>品名</th>
            <th className="amount-cell">USD</th>
            <th className="amount-cell">CNY</th>
            <th>方式</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={10} className="empty-cell">
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
                <td className="strong-cell">{payment.invoiceNo || "-"}</td>
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
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}
