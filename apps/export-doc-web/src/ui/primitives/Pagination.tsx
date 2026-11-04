import { KeyboardEvent, useEffect, useState } from "react";

export function Pagination({
  pageNumber,
  totalPages,
  totalCount,
  pageSize,
  pageSizeOptions = [pageSize],
  isBusy,
  onPageChange,
  onPageSizeChange,
}: {
  pageNumber: number;
  totalPages?: number;
  totalCount: number;
  pageSize: number;
  pageSizeOptions?: readonly number[];
  isBusy: boolean;
  onPageChange: (pageNumber: number) => void;
  onPageSizeChange?: (pageSize: number) => void;
}) {
  const normalizedTotalPages = Math.max(totalPages || Math.ceil(totalCount / pageSize) || 1, 1);
  const normalizedPageNumber = clampPageNumber(pageNumber, normalizedTotalPages);
  const [goToPageText, setGoToPageText] = useState(String(normalizedPageNumber));

  useEffect(() => {
    setGoToPageText(String(normalizedPageNumber));
  }, [normalizedPageNumber]);

  function changePage(nextPageNumber: number) {
    onPageChange(clampPageNumber(nextPageNumber, normalizedTotalPages));
  }

  function submitGoToPage() {
    const nextPageNumber = Number.parseInt(goToPageText, 10);
    if (!Number.isFinite(nextPageNumber)) {
      setGoToPageText(String(normalizedPageNumber));
      return;
    }

    changePage(nextPageNumber);
  }

  function handleGoToPageKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter") {
      event.preventDefault();
      submitGoToPage();
    }
  }

  return (
    <footer className="pagination-bar" data-ui="pagination" role="navigation" aria-label="分页" aria-busy={isBusy}>
      <span>
        第 {normalizedPageNumber} / {normalizedTotalPages} 页 · {totalCount} 条
      </span>
      {onPageSizeChange && pageSizeOptions.length > 1 ? <label className="page-size-control">
        <span>每页</span>
        <select value={pageSize} disabled={isBusy} onChange={(event) => onPageSizeChange(Number(event.target.value))}>
          {pageSizeOptions.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>
        <span>条</span>
      </label> : <span>每页 {pageSize} 条</span>}
      <label className="page-jump-control">
        <span>跳至</span>
        <input
          aria-label="跳转页"
          inputMode="numeric"
          value={goToPageText}
          disabled={isBusy}
          onChange={(event) => setGoToPageText(event.target.value.replace(/[^\d]/g, ""))}
          onKeyDown={handleGoToPageKeyDown}
        />
        <button type="button" disabled={isBusy} onClick={submitGoToPage}>
          跳转
        </button>
      </label>
      <div className="pager-buttons">
        <button type="button" disabled={isBusy || normalizedPageNumber <= 1} onClick={() => changePage(1)}>
          首页
        </button>
        <button type="button" disabled={isBusy || normalizedPageNumber <= 1} onClick={() => changePage(normalizedPageNumber - 1)}>
          上一页
        </button>
        <button
          type="button"
          disabled={isBusy || normalizedPageNumber >= normalizedTotalPages}
          onClick={() => changePage(normalizedPageNumber + 1)}
        >
          下一页
        </button>
        <button
          type="button"
          disabled={isBusy || normalizedPageNumber >= normalizedTotalPages}
          onClick={() => changePage(normalizedTotalPages)}
        >
          末页
        </button>
      </div>
    </footer>
  );
}

function clampPageNumber(value: number, totalPages: number) {
  if (!Number.isFinite(value)) {
    return 1;
  }

  return Math.min(Math.max(Math.trunc(value), 1), Math.max(totalPages, 1));
}
