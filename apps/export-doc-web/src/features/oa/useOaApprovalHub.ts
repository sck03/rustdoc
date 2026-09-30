import { useQueries } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { oaApi } from "./oaApi.ts";
import { oaAccess, oaKinds, oaModules, type OaKind } from "./oaModel.ts";

export function useOaApprovalHub(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const queues: { kind: OaKind; finance: boolean; title: string; description: string; label: string; href: string }[] = oaKinds.filter((kind) => oaAccess(user, kind, "view")).map((kind) => ({
    kind, finance: false, title: oaModules[kind].name, description: oaModules[kind].description,
    label: oaAccess(user, kind, "approve") ? "待我审批" : "我的待审批申请",
    href: `/office/requests/${kind}${oaAccess(user, kind, "approve") ? "?view=approvals" : ""}`,
  }));
  if (oaAccess(user, "expense", "view") && oaAccess(user, "expense", "complete")) {
    queues.push({ kind: "expense", finance: true, title: "财务待接收",
      description: "核对已批准的报销明细、凭证和审批记录，并登记接收。",
      label: "待财务接收", href: "/office/requests/expense?view=finance" });
  }
  const queries = useQueries({ queries: queues.map(({ kind, finance }) => ({
    queryKey: ["office", "oa", "hub", kind, finance, user.id, user.companyScope],
    queryFn: ({ signal }: { signal: AbortSignal }) => oaApi(client, kind).list({
      mineOnly: finance ? false : !oaAccess(user, kind, "approve"), status: finance ? "Approved" : "Pending",
      financeOnly: kind === "expense" ? finance : undefined, pageNumber: 1, pageSize: 5,
      approvalsOnly: !finance && oaAccess(user, kind, "approve"),
    }, { signal }),
    refetchInterval: 30000, refetchIntervalInBackground: false,
  })) });
  return queues.map((queue, index) => ({ ...queue, query: queries[index] }));
}
