import { useQueries } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { isMeetingBooking, officeAccess, type OfficeKind, type OfficePage, type OfficeRequestRow } from "../office/officeModel.ts";

export function useOfficeRequestHub(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const register = user.capabilities.usesOfficeRegister;
  const status = register ? "Approved" : "Pending";
  const queues = (["rooms", "supplies"] as OfficeKind[]).filter(kind => officeAccess(user, kind).allows("view")).map(kind => ({
    kind, finance: false, handling:false, status, title: kind === "rooms" ? "会议室预约" : "物品领用",
    description: kind === "rooms" ? "预约占用时段，使用前交接，结束后归还钥匙或登记结束。" : "批准或登记后预留库存，发放时扣减；借用品支持分次归还，消耗品发放即办结。",
    label: register ? (kind === "rooms" ? "待使用／交接" : officeAccess(user, kind).canSeeOthers ? "待发放" : "我的待领用") : officeAccess(user, kind).canSeeOthers ? "可见待审批申请" : "我的待审批申请",
    href: `/office/${kind === "rooms" ? "meeting-rooms" : "supplies"}?view=requests&status=${status}`,
  }));
  if (officeAccess(user,"supplies").allows("view") && officeAccess(user,"supplies").allows("issue")) {
    for (const [status,title] of [["Approved","物品待发放"],["Issued","借用物品待归还"]]) queues.push({kind:"supplies",finance:false,handling:true,status,title,description:"按物品组分工办理实物交接；未全部归还的借用品继续保留。",label:title,href:`/office/supplies?view=requests&handlingOnly=true&status=${status}`});
  }
  const queries = useQueries({ queries: queues.map(({ kind, handling, status }) => ({
    queryKey: ["office", kind, "hub", user.id, user.companyScope, register, handling, status],
    queryFn: async ({ signal }: { signal: AbortSignal }) => {
      const input = { status, mineOnly: !handling && !officeAccess(user, kind).canSeeOthers, handlingOnly: kind === "supplies" ? handling : undefined, pageNumber: 1, pageSize: 5 };
      const page: OfficePage<OfficeRequestRow> = await (kind === "rooms"
        ? client.listMeetingBookings(input, { signal }) : client.listOfficeSupplyRequests(input, { signal }));
      return { ...page, items: page.items.map(row => ({ id: row.id, employeeName: row.applicantName,
        title: isMeetingBooking(row) ? row.title : `${row.supplyName} · ${row.quantity} ${row.unit}` })) };
    },
    refetchInterval: 30000, refetchIntervalInBackground: false,
  })) });
  return queues.map((queue, index) => ({ ...queue, query: queries[index] }));
}
