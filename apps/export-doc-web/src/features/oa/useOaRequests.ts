import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { oaApi } from "./oaApi.ts";
import { oaAccess, type OaKind } from "./oaModel.ts";

export function useOaRequests(client: ExportDocManagerApiClient, user: ApiUserDto, kind: OaKind) {
  const [params, setParams] = useSearchParams();
  const selected = Number(params.get("requestId")) || 0;
  const [page, setPage] = useState(1);
  const canReceive = kind === "expense" && oaAccess(user, kind, "complete");
  const canApprove = oaAccess(user, kind, "approve");
  const canHandle = kind !== "expense" && oaAccess(user,kind,"complete");
  const view = params.get("view") ?? (canReceive && !canApprove ? "finance" : "requests");
  const financeOnly = canReceive && view === "finance";
  const approvalsOnly = canApprove && view === "approvals";
  const handlingOnly = canHandle && view === "handling";
  const completing = financeOnly || handlingOnly;
  const [mineOnly, setMine] = useState(!oaAccess(user, kind, "approve"));
  const [filter, setFilter] = useState({ view, status: completing ? "Approved" : approvalsOnly ? "Pending" : "" });
  const status = filter.view === view ? filter.status : completing ? "Approved" : approvalsOnly ? "Pending" : "";
  const api = oaApi(client, kind);
  const key = ["office", "oa", kind, user.id, user.companyScope];
  const query = useQuery({ queryKey: [...key, "list", page, mineOnly, status, financeOnly, approvalsOnly, handlingOnly], enabled: oaAccess(user, kind, "view"),
    queryFn: ({ signal }) => api.list({ pageNumber: page, pageSize: 20, mineOnly: completing || approvalsOnly ? false : mineOnly, status: status || undefined, financeOnly: kind === "expense" ? financeOnly : undefined, approvalsOnly, handlingOnly: kind !== "expense" ? handlingOnly : undefined }, { signal }),
    refetchInterval: 30000, refetchIntervalInBackground: false });
  const detail = useQuery({ queryKey: [...key, "detail", selected], enabled: selected > 0 && oaAccess(user, kind, "view"),
    queryFn: ({ signal }) => api.get(selected, { signal }) });
  return { query, detail, selected, page, setPage, mineOnly, status, canReceive, financeOnly, canApprove, approvalsOnly,canHandle,handlingOnly,
    changeView: (value: string) => { setParams({ view: value }); setPage(1); },
    select: (id: number) => setParams((current) => { const next = new URLSearchParams(current); if (id) next.set("requestId", String(id)); else next.delete("requestId"); return next; }),
    changeMine: (value: boolean) => { setMine(value); setPage(1); }, changeStatus: (value: string) => { setFilter({ view, status: value }); setPage(1); } };
}
