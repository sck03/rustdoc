import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import "../../styles/routes/business-records.css";

export function InvoicePicker({ client, userId, onSelect, onClose }: {
  client: ExportDocManagerApiClient; userId: number; onSelect: (invoiceId: number) => void; onClose: () => void;
}) {
  const [input, setInput] = useState("");
  const [keyword, setKeyword] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const query = useQuery({
    queryKey: [...queryKeys.invoicesRoot(), "picker", userId, keyword, pageNumber, pageSize],
    queryFn: ({ signal }) => client.listInvoices({ pageNumber, pageSize, keyword: keyword || undefined }, { signal }),
  });
  useServerPageNumber(query, pageNumber, setPageNumber);
  return <section className="business-records-form" aria-label="选择关联发票">
    <div className="business-records-heading"><h3>选择关联发票</h3>
      <button className="command-button secondary" type="button" onClick={onClose}>取消选择</button></div>
    <p>同号发票的报关数据与实际数据是不同记录，请核对发票类型。</p>
    <form className="toolbar business-records-toolbar" onSubmit={event => { event.preventDefault(); setKeyword(input.trim()); setPageNumber(1); }}>
      <input aria-label="搜索关联发票" placeholder="发票号、合同号或客户" value={input} maxLength={100} onChange={event => setInput(event.target.value)} />
      <button className="command-button secondary" type="submit">搜索发票</button>
    </form>
    {query.isPending ? <PageState tone="loading" title="正在读取发票" /> : query.isError ?
      <PageState tone="error" title="发票读取失败" description={readApiError(query.error)} action={<button className="command-button secondary" type="button" onClick={() => void query.refetch()}>重试</button>} /> : <>
        {!query.data.items.length ? <PageState title="没有匹配的发票" description="请更换搜索词，或先在发票管理中保存发票。" /> :
          <ul className="business-records-list">{query.data.items.map(invoice => <li key={invoice.id} className="business-records-card">
            <div><h3>{invoice.invoiceNo}</h3><p>{invoice.type} · {invoice.customerName || "未填写客户"} · {invoice.invoiceDate}</p></div>
            <button type="button" className="command-button secondary" aria-label={`选择 ${invoice.invoiceNo}（${invoice.type}）`} onClick={() => onSelect(invoice.id)}>选择发票</button>
          </li>)}</ul>}
        <ListPaginationControls pageNumber={pageNumber} pageSize={pageSize} totalCount={query.data.totalCount} totalPages={query.data.totalPages}
          pageSizeOptions={[20, 50, 100]} isBusy={query.isFetching} onPageChange={setPageNumber} onPageSizeChange={size => { setPageSize(size); setPageNumber(1); }} />
      </>}
  </section>;
}
