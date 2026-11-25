import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OaRequest, OaSubmission } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { oaApi } from "./oaApi.ts";
import { useOaOperation } from "./useOaOperation.ts";
import { ApprovalDocument } from "./ApprovalDocument.tsx";
import { OaRequestContent } from "./OaRequestContent.tsx";
import { OaApprovalSteps } from "./OaApprovalSteps.tsx";

export function OaSubmissionHistory({client,user,row}:{client:ExportDocManagerApiClient;user:ApiUserDto;row:OaRequest}) {
  const [page,setPage] = useState(1);
  const [selected,setSelected] = useState(0);
  const api = oaApi(client,row.kind);
  const key = ["office","oa",row.kind,user.id,user.contextId,"submissions",row.id,row.versionNumber];
  const query = useQuery({queryKey:[...key,page],queryFn:({signal})=>api.submissions(row.id,page,{signal})});
  useServerPageNumber(query,page,setPage);
  return <section aria-label="提交版本与原始附件"><h3>提交版本与原始附件</h3>
    {query.isError && <InlineNotice tone="error">{readApiError(query.error)}</InlineNotice>}
    <ul className="oa-submission-list">{query.data?.items.map(item=><li key={item.id}><button type="button" className="command-button secondary" onClick={()=>setSelected(item.id)}>提交版本 {item.submissionNumber} · {formatBusinessDateTime(item.submittedAt,user.businessTimeZone)} · {({Pending:"审批中",Approved:"已批准",Rejected:"已驳回",Withdrawn:"已撤回",Voided:"已作废"} as Record<string,string>)[item.status]??item.status}</button></li>)}</ul>
    <div className="office-card-actions"><button type="button" className="command-button secondary" disabled={page<=1||query.isFetching} onClick={()=>setPage(page-1)}>上一页版本</button><span>第 {page} 页</span><button type="button" className="command-button secondary" disabled={query.isFetching||page*20>=(query.data?.totalCount??0)} onClick={()=>setPage(page+1)}>下一页版本</button></div>
    {selected > 0 && <ApprovalDocument key={selected} api={api} user={user} row={row} scope={row.kind} submissionId={selected}
      renderContent={document => <Snapshot document={document} api={api} row={row} user={user} />} />}
  </section>;
}

function Snapshot({ document, api, row, user }: { document: OaSubmission; api: ReturnType<typeof oaApi>; row: OaRequest; user: ApiUserDto }) {
  const operation = useOaOperation(row.kind);
  const snapshot = document.request;
  if (!snapshot) return null;
  return <article aria-label="提交时的申请资料"><h4>{document.formName} · {snapshot.title}</h4><p>{snapshot.employeeName} · {document.departmentName}</p>
      <OaRequestContent row={snapshot} user={user} />
      <OaApprovalSteps plan={snapshot.approvalPlan} notes={snapshot.approvalRoutingNotes} user={user} />
      <ul>{snapshot.attachments.map(file=><li key={file.id}>{file.fileName} <button type="button" className="command-button secondary" disabled={operation.busy} onClick={()=>void operation.run(signal=>api.download(row.id,file.id,{signal}),blob=>downloadBlob(blob,file.fileName),false)}>下载该版本原文件</button></li>)}</ul>
    {operation.error&&<InlineNotice tone="error">{operation.error}</InlineNotice>}
  </article>;
}
