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
  const query = useQuery({
    queryKey: ["worklist", user.id, source, due, cursors.at(-1), pageSize],
    queryFn: ({ signal }) => client.getWorklist({ source: source || undefined, due, pageNumber, pageSize, cursor: cursors.at(-1) }, { signal }),
    refetchInterval: 30000, refetchIntervalInBackground: false,
  });
  const first = () => setCursors([undefined]);
  return { source, due, pageNumber, pageSize, query, first,
    previous: () => setCursors(current => current.length > 1 ? current.slice(0,-1) : current),
    next: () => { if (!query.isFetching && query.data?.nextCursor) setCursors(current => [...current, query.data!.nextCursor!]); },
    refresh: () => { if (pageNumber > 1) { void queryClient.invalidateQueries({ queryKey: ["worklist", user.id], refetchType: "none" }); first(); } else void query.refetch(); },
    changeSource: (value: string) => { setSource(value); first(); },
    changeDue: (value: WorklistDueFilter) => { setDue(value); first(); },
    changePageSize: (value: number) => { setPageSize(value); first(); } };
}
