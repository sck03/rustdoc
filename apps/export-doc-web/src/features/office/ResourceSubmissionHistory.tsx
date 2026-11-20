import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, ResourceSubmission } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { Button } from "../../ui/Button.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { ApprovalDocument } from "../oa/ApprovalDocument.tsx";
import { OaApprovalSteps } from "../oa/OaApprovalSteps.tsx";
import { useOfficeOperation } from "./useOfficeData.ts";
import { officeRequestApi } from "./officeRequestApi.ts";
import type { OfficeKind, OfficeRequestRow } from "./officeModel.ts";

type Props = { client: ExportDocManagerApiClient; user: ApiUserDto; kind: OfficeKind; row: OfficeRequestRow };
export function ResourceSubmissionHistory(props: Props) {
  const [open, setOpen] = useState(false);
  return <details onToggle={event => setOpen(event.currentTarget.open)}><summary>提交版本</summary>
    {open && <History key={`${props.user.id}:${props.user.contextId}:${props.kind}:${props.row.id}`} {...props} />}
  </details>;
}
function History({ client, user, kind, row }: Props) {
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState(0);
  const api = officeRequestApi(client, kind);
  const query = useQuery({ queryKey: ["office", "resource-submissions", kind, user.id, user.contextId, row.id, row.versionNumber, page],
    queryFn: ({ signal }) => api.submissions(row.id, page, { signal }) });
  useServerPageNumber(query, page, setPage);
  return <section aria-label="资源申请提交版本">
    {query.isPending && <p role="status">正在读取提交版本…</p>}
    {query.isError && <InlineNotice tone="error">{readApiError(query.error)} <Button disabled={query.isFetching} onClick={() => void query.refetch()}>重试</Button></InlineNotice>}
    {query.isSuccess && !query.data.totalCount && <p>暂无提交版本。</p>}
    <ul className="oa-submission-list">{query.data?.items.map(item => <li key={item.id}>
      <Button variant="secondary" onClick={() => setSelected(item.id)}>提交版本 {item.submissionNumber} · {formatBusinessDateTime(item.submittedAt, user.businessTimeZone)}</Button>
    </li>)}</ul>
    <Pagination pageNumber={page} pageSize={20} totalCount={query.data?.totalCount ?? 0} isBusy={query.isFetching} onPageChange={next => { setSelected(0); setPage(next); }} />
    {selected > 0 && <ApprovalDocument key={selected} api={api} user={user} row={row} scope={kind} submissionId={selected}
      renderContent={document => <Snapshot key={document.id} document={document} api={api} row={row} user={user} />} />}
  </section>;
}
function Snapshot({ document, api, row, user }: { document: ResourceSubmission; api: ReturnType<typeof officeRequestApi>; row: OfficeRequestRow; user: ApiUserDto }) {
  const operation = useOfficeOperation([], false);
  const request = document.request;
  if (!request) return null;
  return <article aria-label="提交时的申请资料">
    <h4>{document.formName} · {request.title ?? request.supplyName}</h4>
    <p>{request.employeeName} · {document.departmentName}</p>
    {request.kind === "room" ? <><p>{request.roomName} · {request.attendeeCount ?? "人数受限"} 人</p>
      {request.startsAt && request.endsAt && <p>{formatBusinessDateTime(request.startsAt, user.businessTimeZone)} 至 {formatBusinessDateTime(request.endsAt, user.businessTimeZone)}</p>}</>
      : <><p>{request.quantity ?? "数量受限"} {request.unit}</p><p>{request.purpose}</p></>}
    <OaApprovalSteps plan={request.approvalPlan} user={user} />
    <ul>{request.attachments.map(file => <li key={file.id}>{file.fileName} <Button variant="secondary" disabled={operation.busy}
      onClick={() => void operation.run(signal => api.download(row.id, file.id, { signal }), blob => downloadBlob(blob, file.fileName), false)}>下载该版本原文件</Button></li>)}</ul>
    {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
  </article>;
}
