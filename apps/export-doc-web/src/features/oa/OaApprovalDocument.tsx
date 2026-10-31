import type { ApiUserDto, ExportDocManagerApiClient, OaRequest } from "../../api/index.ts";
import { ApprovalDocument } from "./ApprovalDocument.tsx";
import { oaApi } from "./oaApi.ts";

export function OaApprovalDocument({client,user,row,submissionId = row.submissionId}: {client:ExportDocManagerApiClient;user:ApiUserDto;row:OaRequest;submissionId?:number}) {
  return <ApprovalDocument api={oaApi(client,row.kind)} user={user} row={row} scope={row.kind} submissionId={submissionId} />;
}
