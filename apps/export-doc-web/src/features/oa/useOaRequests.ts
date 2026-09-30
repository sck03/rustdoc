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
  const [financeMode, setFinanceMode] = useState(canReceive && !oaAccess(user, kind, "approve"));
  const financeOnly = canReceive && financeMode;
  const [mineOnly, setMine] = useState(!oaAccess(user, kind, "approve"));
  const [status, setStatus] = useState(financeOnly ? "Approved" : "");
  const api = oaApi(client, kind);
  const key = ["office", "oa", kind, user.id, user.companyScope];
  const query = useQuery({ queryKey: [...key, "list", page, mineOnly, status, financeOnly], enabled: oaAccess(user, kind, "view"),
    queryFn: ({ signal }) => api.list({ pageNumber: page, pageSize: 20, mineOnly: financeOnly ? false : mineOnly, status: status || undefined, financeOnly: kind === "expense" ? financeOnly : undefined }, { signal }),
    refetchInterval: 30000, refetchIntervalInBackground: false });
  const detail = useQuery({ queryKey: [...key, "detail", selected], enabled: selected > 0 && oaAccess(user, kind, "view"),
    queryFn: ({ signal }) => api.get(selected, { signal }) });
  return { query, detail, selected, page, setPage, mineOnly, status, canReceive, financeOnly,
    changeFinance: (value: boolean) => { setFinanceMode(value); setStatus(value ? "Approved" : ""); setPage(1); },
    select: (id: number) => setParams(id ? { requestId: String(id) } : {}),
    changeMine: (value: boolean) => { setMine(value); setPage(1); }, changeStatus: (value: string) => { setStatus(value); setPage(1); } };
}
