import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useOfficeOperation, useOfficePaging } from "../office/useOfficeData.ts";

export function useOrganizationDirectory(client: ExportDocManagerApiClient) {
  return useQuery({ queryKey: queryKeys.organizationDirectory(), queryFn: ({ signal }) => client.getOrganizationDirectory({ signal }) });
}

export function useOrganizationOperation() {
  const queries = useQueryClient();
  const operation = useOfficeOperation();
  function run<T>(command: (signal: AbortSignal) => Promise<T>, done: (result: T) => void) {
    return operation.run(async (signal) => {
      try { return await command(signal); }
      finally {
        await Promise.all([
          queries.invalidateQueries({ queryKey: queryKeys.organizationDirectory() }),
          queries.invalidateQueries({ queryKey: queryKeys.users() }),
        ]);
      }
    }, done);
  }
  return { ...operation, run };
}

export function useOrganizationManagers(client: ExportDocManagerApiClient, companyCode: string) {
  const paging = useOfficePaging();
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const query = useQuery({ queryKey: ["organization-managers", companyCode, search, paging.pageNumber, paging.pageSize],
    queryFn: ({ signal }) => client.listOrganizationManagers({ companyCode, keyword: search || undefined,
      pageNumber: paging.pageNumber, pageSize: paging.pageSize }, { signal }) });
  return { paging, query, keyword, setKeyword, search: () => { setSearch(keyword.trim()); paging.resetPage(); } };
}
