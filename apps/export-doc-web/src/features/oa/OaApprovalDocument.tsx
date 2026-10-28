import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OaRequest } from "../../api/index.ts";
import { useBlobUrl } from "../../ui/useBlobUrl.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeDialog } from "../office/OfficeUi.tsx";
import { useOaOperation } from "./useOaOperation.ts";
import { oaApi } from "./oaApi.ts";

export function OaApprovalDocument({client,user,row,submissionId = row.submissionId}: {client:ExportDocManagerApiClient;user:ApiUserDto;row:OaRequest;submissionId?:number}) {
  const api = oaApi(client,row.kind);
  const operation = useOaOperation(row.kind);
  const [preview,setPreview] = useState<Blob|null>(null);
  const url = useBlobUrl(preview);
  const query = useQuery({queryKey:["office","oa",row.kind,user.id,user.companyScope,"submission",row.id,submissionId,row.versionNumber],enabled:Boolean(submissionId),
    queryFn:({signal})=>api.submission(row.id,submissionId!,{signal}),refetchInterval: query => query.state.data?.documentStatus === "Running" ? 1500 : false,refetchIntervalInBackground:false});
  if (!submissionId) return ["Approved","HandedOff","Completed"].includes(row.status) ? <p className="office-muted">此历史申请未保存提交快照，无法生成可追溯的正式审批单。</p> : null;
  const document = query.data;
  const name = `审批单-${row.id}-v${document?.submissionNumber ?? 1}.pdf`;
  return <section className="oa-detail-content" aria-label="审批单">
    <h3>审批单{document ? ` · 提交版本 ${document.submissionNumber}` : ""}</h3>
    {query.isError && <InlineNotice tone="error">{readApiError(query.error)}</InlineNotice>}
    {document?.documentStatus === "Running" && <p role="status">正在生成审批单 PDF…</p>}
    {document?.documentStatus === "Failed" && <InlineNotice tone="warning">{document.documentError} 已完成的审批结果保留。</InlineNotice>}
    {document?.documentStatus === "Voided" && <InlineNotice tone="warning">此批准已作废，保留历史依据，不能作为有效审批单下载。</InlineNotice>}
    {document?.documentStatus === "NotApproved" && <p>全部必要审批通过后，系统自动生成正式审批单。</p>}
    <div className="office-card-actions">
      {document?.documentStatus === "Failed" && <button type="button" className="command-button" disabled={operation.busy} onClick={()=>void operation.run(signal=>api.generateApproval(row.id,submissionId,{signal}),()=>{void query.refetch();})}>重试生成审批单</button>}
      {document?.documentStatus === "Ready" && <>
        <button type="button" className="command-button secondary" disabled={operation.busy} onClick={()=>void operation.run(signal=>api.approvalPdf(row.id,submissionId,{signal}),blob=>setPreview(new Blob([blob],{type:"application/pdf"})),false)}>预览／打印审批单</button>
        <button type="button" className="command-button" disabled={operation.busy} onClick={()=>void operation.run(signal=>api.approvalPdf(row.id,submissionId,{signal}),blob=>downloadBlob(blob,name),false)}>下载审批单 PDF</button>
        <button type="button" className="command-button secondary" disabled={operation.busy} onClick={()=>void operation.run(signal=>api.materials(row.id,submissionId,{signal}),blob=>downloadBlob(blob,`审批材料-${row.id}-v${document.submissionNumber}.zip`),false)}>下载审批单及原始附件</button>
      </>}
    </div>
    {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
    {preview && document?.documentStatus === "Ready" && <OfficeDialog title="审批单预览与打印" onClose={()=>setPreview(null)}><p>使用 PDF 工具栏打印，或下载后使用本机 PDF 阅读器打印。</p>{url && <iframe title="正式审批单 PDF" src={url} style={{width:"100%",height:"70vh",border:0}} />}</OfficeDialog>}
  </section>;
}
