import { Link } from "react-router-dom";
import { RefreshCw } from "lucide-react";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useWorklist } from "./useWorklist.ts";
import { worklistDueLabel, worklistDueOptions, worklistTarget } from "./worklistModel.ts";
import "../../styles/routes/business-records.css";

export function WorklistPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const model = useWorklist(client, user);
  const { query } = model;
  const data = query.data;
  return <section className="work-surface business-records" aria-label="我的待办">
    <header className="business-records-heading"><div><h2>我的待办</h2>
      <p>按业务来源分组，进入原业务处理后自动更新。到期时间按公司业务时区显示。</p></div>
      <button className="command-button secondary" type="button" disabled={query.isFetching} onClick={() => void query.refetch()}><RefreshCw size={16} aria-hidden="true" />刷新</button></header>
    <div className="business-records-toolbar">
      <label>事项来源<select value={model.source} onChange={(event) => model.changeSource(event.target.value)}>
        <option value="">全部来源</option>{data?.sources.map((source) => <option key={source.key} value={source.key}>{source.name}（{source.count}）</option>)}
      </select></label>
      <label>到期筛选<select value={model.due} onChange={(event) => {
        const option = worklistDueOptions.find((item) => item.value === event.target.value);
        if (option) model.changeDue(option.value);
      }}>{worklistDueOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label>
    </div>
    {query.isPending ? <PageState tone="loading" title="正在读取待办" /> : query.isError ?
      <PageState tone="error" title="待办加载失败" description={readApiError(query.error)} /> : <>
        {!data?.page.items.length && <PageState title="当前没有符合条件的待办" description="仅汇总当前账号有权查看和处理的业务事项。" />}
        <ul className="business-records-list" aria-busy={query.isFetching}>{data?.page.items.map((item) => {
          const target = worklistTarget(item);
          return <li key={`${item.source}:${item.recordId}`} className="business-records-card">
            <div><span className="business-records-muted">{data.sources.find((source) => source.key === item.source)?.name}</span>
              <h3>{item.title}</h3><p>{item.description}</p></div>
            <div className="business-records-actions"><span className="business-records-due" data-overdue={item.isOverdue}>
              {item.isOverdue ? "已到期 · " : ""}{worklistDueLabel(item, user.businessTimeZone)}</span>
              {target && <Link className="command-button secondary" to={target}>查看并处理</Link>}</div>
          </li>;
        })}</ul>
        <ListPaginationControls pageNumber={model.pageNumber} pageSize={model.pageSize} totalCount={data?.page.totalCount ?? 0}
          totalPages={data?.page.totalPages ?? 0} pageSizeOptions={[20, 50, 100]} isBusy={query.isFetching}
          onPageChange={model.setPageNumber} onPageSizeChange={model.changePageSize} />
      </>}
  </section>;
}
