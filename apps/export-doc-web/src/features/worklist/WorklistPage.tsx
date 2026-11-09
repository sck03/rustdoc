import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import { Link } from "react-router-dom";
import { RefreshCw } from "lucide-react";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { Field, FilterBar } from "../../ui/primitives/Field.tsx";
import { PageHeader } from "../../ui/primitives/PageHeader.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useWorklist } from "./useWorklist.ts";
import { worklistDueLabel, worklistDueOptions, worklistTarget } from "./worklistModel.ts";
import "../../styles/routes/business-records.css";

export function WorklistPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const model = useWorklist(client, user);
  const { query } = model;
  const data = query.data;
  return <ListWorkspace className="business-records" aria-label="我的待办">
    <PageHeader title="待办清单" count={data?.page.totalCount} description="按来源和到期时间查找，进入原业务处理后自动更新。" actions={
      <button className="command-button secondary" type="button" disabled={query.isFetching} onClick={model.refresh}><RefreshCw size={16} aria-hidden="true" />刷新</button>}>
    <FilterBar className="list-workspace-filters" label="待办筛选">
      <Field label="事项来源"><select value={model.source} onChange={(event) => model.changeSource(event.target.value)}>
        <option value="">全部来源</option>{data?.sources.map((source) => <option key={source.key} value={source.key}>{source.name}（{source.count}）</option>)}
      </select></Field>
      <Field label="到期筛选"><select value={model.due} onChange={(event) => {
        const option = worklistDueOptions.find((item) => item.value === event.target.value);
        if (option) model.changeDue(option.value);
      }}>{worklistDueOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></Field>
    </FilterBar>
    </PageHeader>
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
        <footer className="pagination-bar" role="navigation" aria-label="待办分页" aria-busy={query.isFetching}>
          <span>第 {model.pageNumber} 页 · {data?.page.totalCount ?? 0} 条</span>
          <label>每页 <select aria-label="每页条数" value={model.pageSize} disabled={query.isFetching} onChange={event=>model.changePageSize(Number(event.target.value))}>
            {[20,50,100].map(size=><option key={size} value={size}>{size}</option>)}
          </select> 条</label>
          <div className="pager-buttons">
            <Button disabled={query.isFetching || model.pageNumber===1} onClick={model.first}>首页</Button>
            <Button disabled={query.isFetching || model.pageNumber===1} onClick={model.previous}>上一页</Button>
            <Button disabled={query.isFetching || !data?.nextCursor} onClick={model.next}>下一页</Button>
          </div>
        </footer>
      </>}
  </ListWorkspace>;
}
