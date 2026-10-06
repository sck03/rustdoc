import { useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient, ListOfficeSuppliesRequest, OfficeRequestEventRecord, OfficeStockMovementRecord } from "../../api/index.ts";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { officeAccess, officeDayRange, officeRequestFocus, type OfficeAction, type OfficeKind, type OfficePage, type OfficeRequestRow } from "./officeModel.ts";

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

export function useOfficeDirectory<T>(user: ApiUserDto, kind: OfficeKind,
  load: (input: ListOfficeSuppliesRequest, signal: AbortSignal) => Promise<OfficePage<T>>) {
  const paging = useOfficePaging();
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const [includeInactive, setIncludeInactive] = useState(false);
  const [lowStockOnly, setLowStockOnly] = useState(false);
  const query = useQuery({
    queryKey: ["office", kind, "resources", user.id, user.companyScope, search, includeInactive, lowStockOnly, paging.pageNumber, paging.pageSize],
    queryFn: ({ signal }) => load({ keyword: search || undefined, includeInactive, lowStockOnly,
      pageNumber: paging.pageNumber, pageSize: paging.pageSize }, signal),
    refetchInterval: 30000, refetchIntervalInBackground: false,
  });
  const commitSearch = () => { setSearch(keyword.trim()); paging.resetPage(); };
  return { paging, query, keyword, search, includeInactive, lowStockOnly, commitSearch,
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
    enabled: Boolean(range), refetchInterval: 30000, refetchIntervalInBackground: false,
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
  return { paging, query };
}

export function useOfficeOperation() {
  const queries = useQueryClient();
  const abortable = useAbortableOperation();
  const inFlight = useRef(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const refresh = () => Promise.all(["office", "worklist"].map(key => queries.invalidateQueries({ queryKey: [key] })));
  async function run<T>(operation: (signal: AbortSignal) => Promise<T>, done: (result: T) => void) {
    if (inFlight.current) return;
    inFlight.current = true;
    setBusy(true);
    setError("");
    try {
      const result = await abortable(operation);
      // Apply the result before refreshes can unmount a deleted or filtered record.
      done(result);
      await refresh();
    } catch (failure) {
      if (!isAbortError(failure)) {
        setError(readApiError(failure));
        void refresh();
      }
    } finally { inFlight.current = false; setBusy(false); }
  }
  return { run, busy, error };
}

export function useOfficeRequests(client: ExportDocManagerApiClient, user: ApiUserDto, kind: OfficeKind) {
  const access = officeAccess(user, kind);
  const [search, setSearch] = useSearchParams();
  const focus = officeRequestFocus(search);
  const focused = Boolean(focus.requestId || focus.applicantUserId || focus.employeeId);
  const handlingOnly = search.get("handlingOnly") === "true" && !focused;
  const paging = useOfficePaging();
  const [mineOnlyFilter, setMineOnly] = useState(!access.canSeeOthers);
  const [statusFilter, setStatus] = useState(search.get("status") ?? (user.capabilities.usesOfficeRegister ? "" : access.allows("approve") ? "Pending" : ""));
  const mineOnly = focused || handlingOnly ? false : mineOnlyFilter;
  const status = focused ? "" : statusFilter;
  const query = useQuery({
    queryKey: ["office", kind, "requests", user.id, user.companyScope, paging.pageNumber, paging.pageSize, mineOnly, status, focus, handlingOnly],
    queryFn: async ({ signal }): Promise<OfficePage<OfficeRequestRow>> => {
      const input = { mineOnly, status: status || undefined, handlingOnly, pageNumber: paging.pageNumber, pageSize: paging.pageSize, ...focus };
      return kind === "rooms" ? client.listMeetingBookings(input, { signal }) : client.listOfficeSupplyRequests(input, { signal });
    },
    refetchInterval: 30000,
    refetchIntervalInBackground: false,
  });
  const clearFocus = () => { setSearch({ view: "requests" }); paging.resetPage(); };
  return { access, paging, query, mineOnly, status, focus, focused, handlingOnly, changeHandling: (value: boolean) => { setSearch(value ? {view:"requests",handlingOnly:"true"} : {view:"requests"}); setStatus(""); paging.resetPage(); }, clearFocus: () => { setStatus(""); setMineOnly(!access.canSeeOthers); clearFocus(); },
    changeMineOnly: (value: boolean) => { setMineOnly(value); clearFocus(); },
    changeStatus: (value: string) => { setStatus(value); if (!handlingOnly) clearFocus(); else paging.resetPage(); } };
}

export function applyOfficeAction(client: ExportDocManagerApiClient, kind: OfficeKind, row: OfficeRequestRow,
  action: OfficeAction, note: string, quantity: number, signal: AbortSignal): Promise<OfficeRequestRow> {
  const init = { signal };
  if (kind === "rooms") {
    const request = { id: row.id, body: { expectedVersion: row.versionNumber, note } };
    switch (action) {
      case "approve": return client.approveMeetingBooking(request, init);
      case "reject": return client.rejectMeetingBooking(request, init);
      case "cancel": return client.cancelMeetingBooking(request, init);
      case "issue": return client.issueMeetingRoomKey(request, init);
      case "return": return client.returnMeetingRoomKey(request, init);
    }
  }
  const request = { id: row.id, body: { expectedVersion: row.versionNumber, note, quantity } };
  switch (action) {
    case "approve": return client.approveOfficeSupplyRequest(request, init);
    case "reject": return client.rejectOfficeSupplyRequest(request, init);
    case "cancel": return client.cancelOfficeSupplyRequest(request, init);
    case "issue": return client.issueOfficeSupply(request, init);
    case "return": return client.returnOfficeSupply(request, init);
  }
}
