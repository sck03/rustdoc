import { queryOptions } from "@tanstack/react-query";
import type { ApiRequestInit, ApiUserDto, OaSubmission } from "../../api/index.ts";
import { queryPollingInterval } from "../../api/queryPollingPolicy.ts";
import { largeDocumentQueryOptions } from "../../api/queryRetention.ts";

export type ApprovalDocumentData = Pick<OaSubmission, "documentStatus" | "documentError" | "submissionNumber" | "materialAccess">;

export function approvalSubmissionOptions<T extends ApprovalDocumentData>(
  api: { submission: (id: number, submissionId: number, init?: ApiRequestInit) => Promise<T> },
  user: ApiUserDto,
  row: { id: number; versionNumber: number },
  scope: string,
  submissionId?: number,
) {
  return queryOptions({
    queryKey: ["office", "approval-document", scope, user.id, user.contextId, row.id, submissionId, row.versionNumber],
    enabled: Boolean(submissionId),
    queryFn: ({ signal }) => api.submission(row.id, submissionId!, { signal }),
    refetchInterval: query => queryPollingInterval(query, query.state.data?.documentStatus === "Running" ? 1500 : false),
    refetchIntervalInBackground: false,
    ...largeDocumentQueryOptions,
  });
}
