import { useQueries } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { isMeetingBooking, officeAccess, type OfficeKind, type OfficePage, type OfficeRequestRow } from "../office/officeModel.ts";

export function useOfficeRequestHub(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const register = user.capabilities.usesOfficeRegister;
  const status = register ? "Approved" : "Pending";
  const queues = (["rooms", "supplies"] as OfficeKind[]).filter(kind => officeAccess(user, kind).allows("view")).map(kind => ({
    kind, finance: false, title: kind === "rooms" ? "会议室预约" : "物品领用",
    description: kind === "rooms" ? "预约占用时段，使用前交接，结束后归还钥匙或登记结束。" : "批准或登记后预留库存，发放时扣减；借用品支持分次归还，消耗品发放即办结。",
    label: register ? (kind === "rooms" ? "待使用／交接" : "待发放") : officeAccess(user, kind).canSeeOthers ? "可见待审批申请" : "我的待审批申请",
    href: `/office/${kind === "rooms" ? "meeting-rooms" : "supplies"}?view=requests&status=${status}`,
  }));
  const queries = useQueries({ queries: queues.map(({ kind }) => ({
    queryKey: ["office", kind, "hub", user.id, user.companyScope, register],
    queryFn: async ({ signal }: { signal: AbortSignal }) => {
      const input = { status, mineOnly: !officeAccess(user, kind).canSeeOthers, pageNumber: 1, pageSize: 5 };
      const page: OfficePage<OfficeRequestRow> = await (kind === "rooms"
        ? client.listMeetingBookings(input, { signal }) : client.listOfficeSupplyRequests(input, { signal }));
      return { ...page, items: page.items.map(row => ({ id: row.id, employeeName: row.applicantName,
        title: isMeetingBooking(row) ? row.title : `${row.supplyName} · ${row.quantity} ${row.unit}` })) };
    },
    refetchInterval: 30000, refetchIntervalInBackground: false,
  })) });
  return queues.map((queue, index) => ({ ...queue, query: queries[index] }));
}
