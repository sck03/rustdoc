import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { oaAccess, oaKinds, oaModules, oaHandlingLabels, type OaKind } from "./oaModel.ts";

export function useOaApprovalHub(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const queues: { kind: OaKind; finance: boolean; handling: boolean; title: string; description: string; label: string; href: string }[] = oaKinds.filter((kind) => oaAccess(user, kind, "view")).map((kind) => ({
    kind, finance: false, handling:false, title: oaModules[kind].name, description: oaModules[kind].description,
    label: oaAccess(user, kind, "approve") ? "待我审批" : "我的待审批申请",
    href: `/office/requests/${kind}${oaAccess(user, kind, "approve") ? "?view=approvals" : ""}`,
  }));
  if (oaAccess(user, "expense", "view") && oaAccess(user, "expense", "complete")) {
    queues.push({ kind: "expense", finance: true, handling:false, title: "财务待接收",
      description: "核对已批准的报销明细、凭证和审批记录，并登记接收。",
      label: "待财务接收", href: "/office/requests/expense?view=finance" });
  }
  for (const kind of oaKinds.filter(kind => kind !== "expense" && kind !== "personnel" && oaAccess(user,kind,"view") && oaAccess(user,kind,"complete"))) {
    queues.push({kind,finance:false,handling:true,title:`${oaModules[kind].name} · ${oaHandlingLabels[kind]}`,description:oaModules[kind].description,label:oaHandlingLabels[kind],href:`/office/requests/${kind}?view=handling`});
  }
  const query = useQuery({
    queryKey: ["office", "oa", "hub", user.id, user.companyScope],
    queryFn: ({ signal }) => client.getOaApprovalHub({}, { signal }),
    enabled: queues.length > 0, staleTime: 30000,
    refetchInterval: 30000, refetchIntervalInBackground: false,
  });
  return queues.map(queue => ({ ...queue, query: { ...query, data: query.data?.entries.find(entry => entry.kind === queue.kind && entry.finance === queue.finance && entry.handling === queue.handling)?.page } }));
}
