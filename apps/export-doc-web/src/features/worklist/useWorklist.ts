import { queryPollingInterval } from "../../api/queryPollingPolicy.ts";
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, WorklistDueFilter } from "../../api/index.ts";

export function useWorklist(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const queryClient = useQueryClient();
  const [source, setSource] = useState("");
  const [due, setDue] = useState<WorklistDueFilter>("All");
  const [cursors, setCursors] = useState<(string | undefined)[]>([undefined]);
  const pageNumber = cursors.length;
  const [pageSize, setPageSize] = useState(20);
  const firstPage = pageNumber === 1;
  const summary = useQuery({
    queryKey: ["worklist", user.id, user.contextId, user.permissionSchemeVersion, "summary", source, due, pageSize],
    queryFn: ({ signal }) => client.getWorklist({ source: source || undefined, due, pageNumber: 1, pageSize, includeSummary: true }, { signal }),
    staleTime: 60_000,
    refetchInterval: query => firstPage ? queryPollingInterval(query, 60_000) : false,
    refetchIntervalInBackground: false,
  });
  const detail = useQuery({
    queryKey: ["worklist", user.id, user.contextId, user.permissionSchemeVersion, source, due, cursors.at(-1), pageSize],
    queryFn: ({ signal }) => client.getWorklist({ source: source || undefined, due, pageNumber, pageSize, cursor: cursors.at(-1), includeSummary: false }, { signal }),
    enabled: !firstPage,
    staleTime: 60_000,
    gcTime: 0,
    refetchIntervalInBackground: false,
  });
  const query = firstPage ? summary : detail;
  const first = () => setCursors([undefined]);
  const totalCount = source ? summary.data?.sources.find(item => item.key === source)?.count : summary.data?.page.totalCount;
  return { source, due, pageNumber, pageSize, query, summary, totalCount, first,
    previous: () => setCursors(current => current.length > 1 ? current.slice(0,-1) : current),
    next: () => { if (!query.isFetching && query.data?.nextCursor) setCursors(current => [...current, query.data!.nextCursor!]); },
    refresh: () => { void summary.refetch(); if (!firstPage) { void queryClient.invalidateQueries({ queryKey: ["worklist", user.id], refetchType: "none" }); first(); } },
    changeSource: (value: string) => { setSource(value); first(); },
    changeDue: (value: WorklistDueFilter) => { setDue(value); first(); },
    changePageSize: (value: number) => { setPageSize(value); first(); } };
}
