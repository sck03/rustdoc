import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import type { ExportDocManagerApiClient, HsCodeDeclarationExample } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useDebouncedValue } from "../../ui/useDebouncedValue.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { formatHsKnowledgeStatus } from "./hsKnowledgeDisplay.ts";
import { useHsExampleWorkspace } from "./useHsExampleWorkspace.ts";

export function ExampleLibrary({ businessYear, client, canOperate, canManage, canBatchManage }: {
  businessYear: number; client: ExportDocManagerApiClient; canOperate: boolean; canManage: boolean; canBatchManage: boolean;
}) {
  const [keyword, setKeyword] = useState("");
  const filter = useDebouncedValue(keyword);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(30);
  const [selected, setSelected] = useState<Map<number, number>>(new Map());
  const workspace = useHsExampleWorkspace(client, businessYear, canOperate);
  const { draft, busy, fieldsLocked } = workspace;
  const data = useQuery({ queryKey: ["hs-knowledge", "examples", filter, pageNumber, pageSize],
    queryFn: ({ signal }) => client.listHsCodeKnowledgeExamples({ keyword: filter, pageNumber, pageSize }, { signal }), placeholderData: keepPreviousData });
  useServerPageNumber(data, pageNumber, setPageNumber);
  useEffect(() => { setPageNumber(1); }, [filter, pageSize]);
  useEffect(() => { setSelected(new Map()); }, [filter, pageSize, pageNumber]);
  function toggle(row: HsCodeDeclarationExample) {
    setSelected(current => {
      const next = new Map(current);
      if (next.has(row.id)) next.delete(row.id); else next.set(row.id, row.versionNumber);
      return next;
    });
  }
  async function removeSelected() {
    if (await workspace.remove([...selected].map(([id, expectedVersion]) => ({ id, expectedVersion })))) setSelected(new Map());
  }
  return <div className="knowledge-task-card">
    <div className="section-heading"><h2>申报实例库</h2>{canOperate ? <Button disabled={busy} onClick={() => workspace.edit()}>新增实例</Button> : null}</div>
    <div className="knowledge-list-toolbar">
      <input className="knowledge-filter" aria-label="搜索申报实例" value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="搜索名称、规格或HS编码"/>
      <Button variant="secondary" disabled={busy || data.isFetching} onClick={() => void data.refetch()}>刷新</Button>
      {canBatchManage && selected.size > 0 ? <Button disabled={busy || data.isPlaceholderData} onClick={() => void removeSelected()}>批量删除（{selected.size}）</Button> : null}
    </div>
    {workspace.feedback ? <InlineNotice tone={workspace.feedback.tone}>{workspace.feedback.message}</InlineNotice> : null}
    {draft ? <form className="knowledge-editor" onSubmit={event => { event.preventDefault(); workspace.save(); }}>
      <label>商品名称<input disabled={fieldsLocked} required maxLength={300} value={draft.productName} onChange={event => workspace.change({ ...draft, productName: event.target.value })}/></label>
      <label>历史/原始编码<input disabled={fieldsLocked} required maxLength={20} value={draft.rawReportedHsCode} onChange={event => workspace.change({ ...draft, rawReportedHsCode: event.target.value })}/></label>
      <label>当前有效编码<input disabled={fieldsLocked} maxLength={20} value={draft.resolvedCurrentHsCode} onChange={event => workspace.change({ ...draft, resolvedCurrentHsCode: event.target.value })}/></label>
      <label>来源年份<input disabled={fieldsLocked} type="number" min={2000} max={2100} value={draft.sourceYear ?? ""} onChange={event => workspace.change({ ...draft, sourceYear: event.target.value ? Number(event.target.value) : null })}/></label>
      <label className="wide">规格与申报要素<textarea disabled={fieldsLocked} maxLength={1500} value={draft.specification} onChange={event => workspace.change({ ...draft, specification: event.target.value })}/></label>
      <div className="wide form-actions"><Button type="submit" disabled={busy || !canOperate}>保存实例</Button><Button variant="secondary" disabled={busy} onClick={() => workspace.edit(null)}>关闭</Button></div>
    </form> : null}
    {data.isLoading ? <PageState tone="loading" title="正在读取申报实例"/> : null}
    {data.isError ? <PageState tone="error" title="申报实例读取失败" description={readApiError(data.error)}/> : null}
    <div className="knowledge-table">{data.data?.items.map(item => <article key={item.id}>
      {canBatchManage ? <input type="checkbox" aria-label={`选择 ${item.productName}`} disabled={busy || data.isPlaceholderData} checked={selected.has(item.id)} onChange={() => toggle(item)}/> : null}
      <div><strong>{item.productName}</strong><span>{item.rawReportedHsCode}{item.resolvedCurrentHsCode && item.resolvedCurrentHsCode !== item.rawReportedHsCode ? ` → ${item.resolvedCurrentHsCode}` : ""}</span><p>{item.specification}</p><small>{item.source}{item.sourceYear ? ` · ${item.sourceYear}年` : ""}</small></div>
      <div><span className="status-pill">{formatHsKnowledgeStatus(item.resolutionStatus)}</span>
        {canOperate ? <Button variant="secondary" disabled={busy || data.isPlaceholderData} onClick={() => workspace.edit(item)}>编辑</Button> : null}
        {canManage ? <Button variant="text" disabled={busy || data.isPlaceholderData} onClick={() => void workspace.remove([{ id: item.id, expectedVersion: item.versionNumber }])}>删除</Button> : null}
      </div>
    </article>)}</div>
    {data.data?.totalCount === 0 ? <PageState title="暂无正式申报实例"/> : null}
    <Pagination pageNumber={pageNumber} totalPages={Math.max(1, Math.ceil((data.data?.totalCount ?? 0) / pageSize))} totalCount={data.data?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={data.isFetching || busy} onPageChange={setPageNumber} onPageSizeChange={setPageSize}/>
  </div>;
}
