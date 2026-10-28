import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OaRequest } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeDialog } from "../office/OfficeUi.tsx";
import { oaApi } from "./oaApi.ts";
import { OaApprovalSteps } from "./OaApprovalSteps.tsx";

export function OaApprovalPreview({ client, user, row, onClose }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: OaRequest; onClose: () => void }) {
  const query = useQuery({ queryKey: ["office", "oa", row.kind, user.id, user.companyScope, "approval-preview", row.id, row.versionNumber],
    queryFn: ({ signal }) => oaApi(client, row.kind).previewApproval(row.id, { signal }), staleTime: 0, gcTime: 0, retry: false });
  return <OfficeDialog title="提交前审批流程预览" onClose={onClose}>
    <p>依据已保存草稿和当前组织／流程配置解析；正式提交会重新检查并冻结流程。预览不会提交申请。</p>
    {query.isFetching && <p role="status">正在解析审批流程…</p>}
    {query.isError && <InlineNotice tone="error">{readApiError(query.error)}</InlineNotice>}
    {query.data && !query.isError && !query.isFetching && <OaApprovalSteps plan={query.data.approvalPlan} notes={query.data.approvalRoutingNotes} user={user} />}
    <button type="button" className="command-button secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>重新核对流程</button>
  </OfficeDialog>;
}
