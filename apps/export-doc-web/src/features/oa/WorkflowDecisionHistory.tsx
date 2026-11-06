import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto,ExportDocManagerApiClient,OaRequest } from "../../api/index.ts";
import type { OfficeRequestRow } from "../office/officeModel.ts";
import { oaApi } from "./oaApi.ts";
import { officeRequestApi } from "../office/officeRequestApi.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
export function WorkflowDecisionHistory({client,user,row}:{client:ExportDocManagerApiClient;user:ApiUserDto;row:OaRequest|OfficeRequestRow}) {
  const [page,setPage]=useState(1);
  const kind="meetingRoomId" in row?"room":"officeSupplyId" in row?"supply":row.kind;
  const api="meetingRoomId" in row?officeRequestApi(client,"rooms"):"officeSupplyId" in row?officeRequestApi(client,"supplies"):oaApi(client,row.kind);
  const query=useQuery({queryKey:["office","decisions",kind,row.id,row.versionNumber,user.contextId,page],queryFn:({signal})=>api.decisions(row.id,page,{signal}),enabled:Boolean(row.submissionNumber),refetchOnWindowFocus:false});
  return <section aria-label="原始签署与核验历史"><h3>原始签署与核验历史</h3><p className="office-muted">每次真实签署独立保存。沿用旧决定作为引用显示，重审不会覆盖原意见。</p>{query.isError&&<InlineNotice tone="error">{readApiError(query.error)}</InlineNotice>}<ol className="oa-history">{query.data?.items.map(item=><li key={`${item.generation}:${item.taskKey}`}><strong>执行 {item.generation} · {item.signature.approverName}</strong> · {item.signature.status} · 实际签署 {item.signature.actedByName}<p>{formatBusinessDateTime(item.signature.actedAt,user.businessTimeZone)} · {item.signature.note}</p><dl>{Object.entries(item.signature.verificationValues??{}).map(([key,value])=><div key={key}><dt>{key}</dt><dd>{String(value)}</dd></div>)}</dl></li>)}</ol><div className="office-card-actions"><button type="button" className="command-button secondary" disabled={page===1||query.isFetching} onClick={()=>setPage(p=>p-1)}>上一页签署</button><span>第 {page} 页 · {query.data?.totalCount??0} 次真实签署</span><button type="button" className="command-button secondary" disabled={query.isFetching||page*20>=(query.data?.totalCount??0)} onClick={()=>setPage(p=>p+1)}>下一页签署</button></div></section>;
}
