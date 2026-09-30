import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useDebouncedValue } from "../../ui/useDebouncedValue.ts";

export function useEmployeeSearch(client: ExportDocManagerApiClient, user: ApiUserDto, enabled: boolean) {
  const [keyword, setKeyword] = useState("");
  const [composing, setComposing] = useState(false);
  const search = useDebouncedValue(keyword.trim(), 250);
  const query = useQuery({
    queryKey: ["office", "people", "picker", user.id, user.companyScope, user.departmentId, search],
    queryFn: ({ signal }) => client.listPersonnel({ keyword: search, pageSize: 50 }, { signal }),
    enabled: enabled && !composing,
  });
  const waiting = composing || search !== keyword.trim() || query.isFetching;
  return { keyword, setKeyword, composing, setComposing, query, waiting, items: waiting || query.isError ? [] : query.data?.items ?? [] };
}
