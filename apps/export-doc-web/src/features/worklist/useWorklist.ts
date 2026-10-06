import { useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, WorklistDueFilter } from "../../api/index.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";

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
  useServerPageNumber({ ...query, data: query.data?.page }, pageNumber, setPageNumber);
  return { source, due, pageNumber, pageSize, query, setPageNumber,
    changeSource: (value: string) => { setSource(value); setPageNumber(1); },
    changeDue: (value: WorklistDueFilter) => { setDue(value); setPageNumber(1); },
    changePageSize: (value: number) => { setPageSize(value); setPageNumber(1); } };
}
