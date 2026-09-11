import { useQuery } from "@tanstack/react-query";
import { RefreshCw } from "lucide-react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useDirectoryLocation } from "../../ui/useDirectoryLocation.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { readApiError } from "../../ui/formUtils.ts";

const statusLabels: Record<string, string> = { Sent: "已发送", Attempting: "投递中", Uncertain: "结果不确定" };

export function EmailDeliveryHistory({ client, businessTimeZone }: { client: ExportDocManagerApiClient; businessTimeZone: string }) {
  const state = useDirectoryLocation("mail");
  const { keyword, status, pageNumber, pageSize } = state;
  const query = useQuery({
    queryKey: [...queryKeys.emailDeliveries(), keyword, status, pageNumber, pageSize],
    queryFn: ({ signal }) => client.listEmailDeliveries({ keyword, status, pageNumber, pageSize }, { signal }),
  });
  return <section className="form-section" aria-label="邮件投递记录">
    <div className="section-header"><h2>投递记录</h2><span>查看当前授权范围内的邮件历史</span></div>
    <form className="toolbar" onSubmit={(event) => { event.preventDefault(); state.setKeyword(state.keywordInput); }}>
      <input type="search" aria-label="搜索投递记录" placeholder="收件人或主题" value={state.keywordInput} maxLength={100} onChange={(event) => state.setKeywordInput(event.target.value)} />
      <select aria-label="投递状态" value={status} onChange={(event) => state.setStatus(event.target.value)}>
        <option value="">全部状态</option>{Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </select>
      <button className="command-button secondary" type="submit">查询</button>
      <button className="icon-button" type="button" aria-label="刷新投递记录" disabled={query.isFetching} onClick={() => void query.refetch()}><RefreshCw size={17} aria-hidden="true" /></button>
    </form>
    {query.isError ? <PageState tone="error" title="投递记录读取失败" description={readApiError(query.error)} /> : <ResponsiveTableFrame label="邮件投递记录" busy={query.isFetching}>
      <table><thead><tr><th>时间</th><th>来源</th><th>收件人</th><th>主题</th><th>附件</th><th>状态</th></tr></thead>
        <tbody>{query.data?.items.map((row) => <tr key={`${row.deliveryId}-${row.createdAt}`}>
          <td>{formatBusinessDateTime(row.createdAt, businessTimeZone)}</td><td>{row.kind === "ReportDocumentEmail" ? "发票单据" : "通用邮件"}</td>
          <td>{row.recipient}</td><td title={row.errorMessage || row.subject}>{row.subject || "-"}</td><td>{row.attachmentCount}</td>
          <td title={row.errorMessage}>{statusLabels[row.status] ?? row.status}</td>
        </tr>)}{!query.data?.items.length && <tr><td className="empty-cell" colSpan={6}>{query.isFetching ? "正在加载" : "没有符合条件的投递记录"}</td></tr>}</tbody>
      </table>
    </ResponsiveTableFrame>}
    <ListPaginationControls pageNumber={pageNumber} pageSize={pageSize} totalCount={query.data?.totalCount ?? 0} totalPages={query.data?.totalPages ?? 0}
      pageSizeOptions={[20, 50, 100]} isBusy={query.isFetching} onPageChange={state.setPageNumber} onPageSizeChange={state.setPageSize} />
  </section>;
}
