import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { oaAccess, oaKinds, oaModules, oaHandlingLabels, type OaKind } from "./oaModel.ts";
import { officeAccess, type OfficeKind } from "../office/officeModel.ts";

export function useOaApprovalHub(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const queues: { kind: OaKind | OfficeKind; finance: boolean; handling: boolean; status: string; title: string; description: string; label: string; href: string }[] = oaKinds.filter((kind) => oaAccess(user, kind, "view")).map((kind) => ({
    kind, finance: false, handling:false, status:"Pending", title: oaModules[kind].name, description: oaModules[kind].description,
    label: oaAccess(user, kind, "approve") ? "待我审批" : "我的待审批申请",
    href: `/office/requests/${kind}${oaAccess(user, kind, "approve") ? "?view=approvals" : ""}`,
  }));
  if (oaAccess(user, "expense", "view") && oaAccess(user, "expense", "complete")) {
    queues.push({ kind: "expense", finance: true, handling:false, status:"Approved", title: "财务待接收",
      description: "核对已批准的报销明细、凭证和审批记录，并登记接收。",
      label: "待财务接收", href: "/office/requests/expense?view=finance" });
  }
  for (const kind of oaKinds.filter(kind => kind !== "expense" && kind !== "personnel" && oaAccess(user,kind,"view") && oaAccess(user,kind,"complete"))) {
    queues.push({kind,finance:false,handling:true,status:"Approved",title:`${oaModules[kind].name} · ${oaHandlingLabels[kind]}`,description:oaModules[kind].description,label:oaHandlingLabels[kind],href:`/office/requests/${kind}?view=handling`});
  }
  for (const kind of ["rooms", "supplies"] as const) {
    const access = officeAccess(user,kind);
    if (!access.allows("view")) continue;
    const room = kind === "rooms";
    const route = `/office/${room ? "meeting-rooms" : "supplies"}?view=requests`;
    queues.push({kind,finance:false,handling:false,status:"Pending",title:room ? "会议室预约" : "物品领用",description:room ? "审批通过后办理场地交接，结束使用后登记归还。" : "全部审批通过后预留库存，发放与归还分别留痕。",label:access.allows("approve") ? "待我审批" : "我的待审批申请",href:`${route}&status=Pending${access.allows("approve") ? "&approvalsOnly=true" : ""}`});
    for (const [status,title,action] of room ? [["Approved","会议室待交接","issue"],["InUse","会议室待结束／归还","return"]] as const : [["Approved","物品待发放","issue"],["Issued","借用物品待归还","return"]] as const) {
      if (access.allows(action)) queues.push({kind,finance:false,handling:true,status,title,description:"按资源分工办理交接，保留实际操作人及处理记录。",label:title,href:`${route}&handlingOnly=true&status=${status}`});
    }
  }
  const query = useQuery({
    queryKey: ["office", "oa", "hub", user.id, user.companyScope],
    queryFn: ({ signal }) => client.getOaApprovalHub({}, { signal }),
    enabled: queues.length > 0, staleTime: 30000,
    refetchInterval: 30000, refetchIntervalInBackground: false,
  });
  return queues.map(queue => ({ ...queue, query: { ...query, data: query.data?.entries.find(entry => entry.kind === queue.kind && entry.finance === queue.finance && entry.handling === queue.handling && entry.status === queue.status)?.page } }));
}
