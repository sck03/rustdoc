import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import type { ExportDocManagerApiClient, HsCodeRemoteCandidate } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { SegmentedNav } from "../../ui/primitives/Tabs.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useDebouncedValue } from "../../ui/useDebouncedValue.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { HsRemoteCandidateCard } from "./HsRemoteCandidateCard.tsx";

export function RemoteCandidateReview({ client, canOperate, canBatchOperate }: { client: ExportDocManagerApiClient; canOperate: boolean; canBatchOperate: boolean }) {
  const [status, setStatus] = useState("Pending");
  const [keyword, setKeyword] = useState("");
  const filter = useDebouncedValue(keyword);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(30);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [drafts, setDrafts] = useState<Record<number,string>>({});
  const [feedback, setFeedback] = useState<{tone:"success"|"error"; message:string} | null>(null);
  const queryClient = useQueryClient();
  const scope = useScopedSave(JSON.stringify([status, filter, pageNumber, pageSize, canOperate]), client);
  const candidates = useQuery({ queryKey:["hs-remote-candidates",status,filter,pageNumber,pageSize],
    queryFn:({signal})=>client.listHsCodeRemoteCandidates({status,keyword:filter,pageNumber,pageSize},{signal}) });
  useServerPageNumber(candidates,pageNumber,setPageNumber);
  useEffect(()=>{setPageNumber(1);},[status,filter,pageSize]);
  useEffect(()=>{setSelected(new Set());setDrafts({});},[status,filter,pageNumber,pageSize]);
  const guard = useUnsavedChangesGuard({ isDirty: Object.keys(drafts).length > 0 || scope.isSaving });
  const code = (row:HsCodeRemoteCandidate) => drafts[row.id] ?? row.suggestedCurrentHsCode ?? "";
  async function changePage(action:()=>void) {
    if (scope.isSaving || !await guard.confirmDiscardChanges("切换候选列表")) return;
    action();
  }
  async function run(rows:HsCodeRemoteCandidate[], action:"confirm"|"ignore"|"reset") {
    if (!canOperate || !rows.length) return;
    if (action==="confirm" && rows.some(row=>!code(row).trim())) {
      setFeedback({tone:"error",message:"请先为选中的候选填写当前有效编码。"}); return;
    }
    try {
      await scope.run(async({signal,checkCurrent})=>{
        const response = action==="reset"
          ? await client.resetHsCodeRemoteCandidates({body:{ids:rows.map(row=>row.id)}},{signal})
          : await client.reviewHsCodeRemoteCandidatesBatch({body:{items:rows.map(row=>({id:row.id,currentCode:action==="confirm"?code(row).trim():"",confirmed:action==="confirm"}))}},{signal});
        checkCurrent();
        setDrafts(current=>{const next={...current};for(const row of rows)delete next[row.id];return next;});
        setSelected(new Set());
        setFeedback({tone:"success",message:response.message});
        await queryClient.invalidateQueries({queryKey:["hs-remote-candidates"]});
        await queryClient.invalidateQueries({queryKey:["hs-knowledge","examples"]});
      });
    } catch(error) { if(!isAbortError(error))setFeedback({tone:"error",message:readApiError(error)}); }
  }
  const selectedRows=(candidates.data?.items??[]).filter(row=>selected.has(row.id));
  return <section className="remote-candidate-review">
    <div className="section-heading"><h2>联网候选审核</h2><Button variant="secondary" disabled={scope.isSaving} onClick={()=>void candidates.refetch()}>刷新</Button></div>
    <SegmentedNav label="候选审核状态" value={status} onChange={value=>void changePage(()=>setStatus(value))} items={[{key:"Pending",label:"待审核"},{key:"Confirmed",label:"已确认"},{key:"Ignored",label:"已忽略"}]}/>
    <div className="knowledge-list-toolbar">
      <input className="knowledge-filter" aria-label="筛选联网候选" disabled={scope.isSaving || Object.keys(drafts).length>0} value={keyword} onChange={event=>setKeyword(event.target.value)} placeholder="筛选品名、规格、查询词或编码"/>
      {canBatchOperate && selectedRows.length>0 ? <div className="candidate-batch-actions">
        {status==="Pending" ? <><Button disabled={scope.isSaving} onClick={()=>void run(selectedRows,"confirm")}>批量确认（{selectedRows.length}）</Button><Button disabled={scope.isSaving} onClick={()=>void run(selectedRows,"ignore")}>批量忽略</Button></>
          : <Button disabled={scope.isSaving} onClick={()=>void run(selectedRows,"reset")}>恢复待审核（{selectedRows.length}）</Button>}
      </div>:null}
    </div>
    {feedback?<InlineNotice tone={feedback.tone}>{feedback.message}</InlineNotice>:null}
    {candidates.isLoading?<PageState tone="loading" title="正在读取联网候选"/>:null}
    {candidates.isError?<InlineNotice tone="error">{readApiError(candidates.error)}</InlineNotice>:null}
    <div className="knowledge-table">{candidates.data?.items.map(row=><HsRemoteCandidateCard key={row.id} client={client} candidate={row} status={status} canOperate={canOperate} allowSelection={canBatchOperate} selected={selected.has(row.id)} draft={code(row)} isBusy={scope.isSaving}
      onToggle={()=>setSelected(current=>{const next=new Set(current);if(next.has(row.id))next.delete(row.id);else next.add(row.id);return next;})}
      onDraft={value=>setDrafts(current=>{const next={...current};if(value===(row.suggestedCurrentHsCode??""))delete next[row.id];else next[row.id]=value;return next;})}
      onConfirm={()=>void run([row],"confirm")} onIgnore={()=>void run([row],"ignore")} onReset={()=>void run([row],"reset")}/>)}</div>
    {candidates.data?.totalCount===0?<PageState title="当前分类暂无记录"/>:null}
    <Pagination pageNumber={pageNumber} totalPages={Math.max(1,Math.ceil((candidates.data?.totalCount??0)/pageSize))} totalCount={candidates.data?.totalCount??0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={candidates.isFetching||scope.isSaving}
      onPageChange={value=>void changePage(()=>setPageNumber(value))} onPageSizeChange={value=>void changePage(()=>setPageSize(value))}/>
  </section>;
}
