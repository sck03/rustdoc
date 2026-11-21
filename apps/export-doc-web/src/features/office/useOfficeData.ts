import { acknowledgedCommands, officeCommand } from "./officeCommands.ts";
import { officePolling } from "../../api/queryPollingPolicy.ts";
import { useState } from "react";
import { useQuery, useQueryClient, type QueryKey } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient, ListOfficeSuppliesRequest, OaAction, OfficeRequestEventRecord, OfficeStockMovementRecord } from "../../api/index.ts";
import { officeResourceQueryRoots } from "../../api/queryKeys.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { officeAccess, officeDayRange, officeRequestFocus, type OfficeAction, type OfficeKind, type OfficePage, type OfficeRequestRow } from "./officeModel.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";

export function useOfficeView() {
  const [search, setSearch] = useSearchParams();
  const focus = officeRequestFocus(search);
  const records = search.get("view") === "requests" || Boolean(focus.requestId || focus.applicantUserId || focus.employeeId);
  return { records, setRecords: (value: boolean) => setSearch(value ? { view: "requests" } : {}) };
}

export function useOfficePaging() {
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(24);
  return { pageNumber, pageSize, setPageNumber, resetPage: () => setPageNumber(1),
    changePageSize: (size: number) => { setPageSize(size); setPageNumber(1); } };
}

export function useOfficeDirectory<T extends { id: number }>(user: ApiUserDto, kind: OfficeKind,
  load: (input: ListOfficeSuppliesRequest, signal: AbortSignal) => Promise<OfficePage<T>>) {
  const queries = useQueryClient();
  const paging = useOfficePaging();
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const [includeInactive, setIncludeInactive] = useState(false);
  const [lowStockOnly, setLowStockOnly] = useState(false);
  const query = useQuery({
    queryKey: ["office", kind, "resources", user.id, user.companyScope, search, includeInactive, lowStockOnly, paging.pageNumber, paging.pageSize],
    queryFn: ({ signal }) => load({ keyword: search || undefined, includeInactive, lowStockOnly,
      pageNumber: paging.pageNumber, pageSize: paging.pageSize }, signal),
    ...officePolling,
  });
  const commitSearch = () => { setSearch(keyword.trim()); paging.resetPage(); };
  useServerPageNumber(query, paging.pageNumber, paging.setPageNumber);
  const acceptSaved = (saved: T) => queries.setQueriesData<OfficePage<T>>(
    { queryKey: ["office", kind, "resources", user.id, user.companyScope] },
    page => page ? { ...page, items: page.items.map(item => item.id === saved.id ? saved : item) } : page);
  return { paging, query, keyword, search, includeInactive, lowStockOnly, commitSearch, acceptSaved,
    changeKeyword: (value: string) => { setKeyword(value); if (!value) { setSearch(""); paging.resetPage(); } },
    changeInactive: (value: boolean) => { setIncludeInactive(value); paging.resetPage(); },
    changeLowStock: (value: boolean) => { setLowStockOnly(value); paging.resetPage(); },
    refresh: () => { if (keyword.trim() !== search) commitSearch(); else void query.refetch(); } };
}

export function useMeetingAvailability(client: ExportDocManagerApiClient, user: ApiUserDto, roomId: number, day: string) {
  const range = officeDayRange(day, user.businessTimeZone);
  const query = useQuery({
    queryKey: ["office", "availability", user.id, user.companyScope, roomId, range?.from, range?.to],
    queryFn: ({ signal }) => {
      if (!range) throw new Error("请选择有效日期。");
      return client.getMeetingRoomAvailability({ id: roomId, ...range }, { signal });
    },
    enabled: Boolean(range), ...officePolling,
  });
  return { range, query };
}

export function useOfficeHistory(client: ExportDocManagerApiClient, user: ApiUserDto, kind: OfficeKind | "stock", id: number) {
  const paging = useOfficePaging();
  const query = useQuery({
    queryKey: ["office", "history", kind, id, user.id, user.companyScope, paging.pageNumber, paging.pageSize],
    queryFn: async ({ signal }): Promise<OfficePage<OfficeRequestEventRecord | OfficeStockMovementRecord>> => {
      const input = { id, pageNumber: paging.pageNumber, pageSize: paging.pageSize };
      return kind === "stock" ? client.getOfficeStockHistory(input, { signal }) : kind === "rooms"
        ? client.getMeetingBookingHistory(input, { signal }) : client.getOfficeSupplyRequestHistory(input, { signal });
    },
  });
  useServerPageNumber(query, paging.pageNumber, paging.setPageNumber);
  return { paging, query };
}

export function useOfficeOperation(relatedKeys: readonly QueryKey[] = [], includeOffice = true, scope = "office", owner?: unknown) {
  const queries = useQueryClient();
  const [error, setError] = useState("");
  const keys = [...(includeOffice ? [["office"]] : []), ["worklist"], ...relatedKeys];
  const command = useScopedMutation<() => void, { execute: (signal: AbortSignal) => Promise<() => void>; refreshAfter: boolean }>(scope, owner ?? queries, {
    mutationFn: async (input, signal, { checkCurrent }) => {
      setError("");
      try { return await input.execute(signal); }
      catch (failure) {
        checkCurrent();
        if (input.refreshAfter) void Promise.all(keys.map(queryKey => queries.invalidateQueries({ queryKey }))).catch(() => {});
        throw failure;
      }
    },
    onSuccess: done => done(),
    refreshKeys: (_, input) => input.refreshAfter ? keys : [],
    onRefreshError: failure => setError(`操作已成功，列表刷新失败：${readApiError(failure)}`),
    onError: failure => setError(readApiError(failure)),
  });
  async function run<T>(operation: (signal: AbortSignal) => Promise<T>, done: (result: T) => void, refreshAfter = true) {
    await command.mutateAsync({ refreshAfter, execute: async signal => {
      const result = await operation(signal);
      return () => done(result);
    } });
  }
  return { run, busy: command.isPending, error };
}

export const useOfficeResourceOperation = (kind: OfficeKind, client: ExportDocManagerApiClient, recordId = 0) =>
  useOfficeOperation(officeResourceQueryRoots(kind), false, `${kind}:${recordId}`, client);

export function useOfficeRequests(client: ExportDocManagerApiClient, user: ApiUserDto, kind: OfficeKind) {
  const access = officeAccess(user, kind);
  const [search, setSearch] = useSearchParams();
  const focus = officeRequestFocus(search);
  const focused = Boolean(focus.requestId || focus.applicantUserId || focus.employeeId);
  const handlingOnly = search.get("handlingOnly") === "true" && !focused;
  const approvalsOnly = search.get("approvalsOnly") === "true" && !focused;
  const paging = useOfficePaging();
  const [mineOnlyFilter, setMineOnly] = useState(!access.canSeeOthers && !access.allows("approve"));
  const statusFilter = search.get("status") ?? (access.allows("approve") ? "Pending" : "");
  const mineOnly = focused || handlingOnly || approvalsOnly ? false : mineOnlyFilter;
  const status = focused ? "" : approvalsOnly ? "Pending" : statusFilter;
  const query = useQuery({
    queryKey: ["office", kind, "requests", user.id, user.companyScope, paging.pageNumber, paging.pageSize, mineOnly, status, focus, handlingOnly, approvalsOnly],
    queryFn: async ({ signal }): Promise<OfficePage<OfficeRequestRow>> => {
      const input = { mineOnly, status: status || undefined, handlingOnly, approvalsOnly, pageNumber: paging.pageNumber, pageSize: paging.pageSize, ...focus };
      return kind === "rooms" ? client.listMeetingBookings(input, { signal }) : client.listOfficeSupplyRequests(input, { signal });
    },
    ...officePolling,
  });
  const showRequests = (nextStatus: string, handling = false, approvals = false) => {
    setSearch({ view: "requests", status: nextStatus, ...(handling ? { handlingOnly: "true" } : {}), ...(approvals ? { approvalsOnly: "true" } : {}) });
    paging.resetPage();
  };
  useServerPageNumber(query, paging.pageNumber, paging.setPageNumber);
  return { access, paging, query, mineOnly, status, focus, focused, handlingOnly, approvalsOnly,
    changeApprovals: (value: boolean) => showRequests("Pending", false, value),
    changeHandling: (value: boolean) => showRequests("", value),
    clearFocus: () => { setMineOnly(!access.canSeeOthers && !access.allows("approve")); showRequests(""); },
    changeMineOnly: (value: boolean) => { setMineOnly(value); showRequests(status); },
    changeStatus: (value: string) => showRequests(value, handlingOnly, approvalsOnly && value === "Pending") };
}

export function applyOfficeAction(client: ExportDocManagerApiClient, kind: OfficeKind, row: OfficeRequestRow,
  action: OfficeAction, note: string, quantity: number, signal: AbortSignal, taskKey?: string, verification: Pick<OaAction, "verificationValues" | "confirmedReviewPoints" | "nodeKey"> = {}): Promise<OfficeRequestRow> {
  const rooms = kind === "rooms";
  const name = rooms ? "MeetingBooking" : "OfficeSupplyRequest";
  const method = {approve:`approve${name}`,reject:`reject${name}`,cancel:`cancel${name}`,issue:rooms?"issueMeetingRoomKey":"issueOfficeSupply",return:rooms?"returnMeetingRoomKey":"returnOfficeSupply"} as const;
  const body = officeCommand(client,`${kind}:${row.id}:${action}`,{commandKey:undefined,expectedVersion:row.versionNumber,note,taskKey,...verification,...(rooms?{}:{quantity})});
  return acknowledgedCommands(client,name)(row.id,body,{signal},(request,init)=>client[method[action]](request,init));
}
