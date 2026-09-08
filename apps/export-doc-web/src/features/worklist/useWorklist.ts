import { useEffect, useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, WorklistDueFilter } from "../../api/index.ts";

export function useWorklist(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const [source, setSource] = useState("");
  const [due, setDue] = useState<WorklistDueFilter>("All");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const query = useQuery({
    queryKey: ["worklist", user.id, source, due, pageNumber, pageSize],
    queryFn: ({ signal }) => client.getWorklist({ source: source || undefined, due, pageNumber, pageSize }, { signal }),
    refetchInterval: 30000, refetchIntervalInBackground: false, placeholderData: keepPreviousData,
  });
  useEffect(() => {
    if (query.data && !query.isPlaceholderData && pageNumber > Math.max(1, query.data.page.totalPages))
      setPageNumber(Math.max(1, query.data.page.totalPages));
  }, [query.data, query.isPlaceholderData, pageNumber]);
  return { source, due, pageNumber, pageSize, query, setPageNumber,
    changeSource: (value: string) => { setSource(value); setPageNumber(1); },
    changeDue: (value: WorklistDueFilter) => { setDue(value); setPageNumber(1); },
    changePageSize: (value: number) => { setPageSize(value); setPageNumber(1); } };
}
