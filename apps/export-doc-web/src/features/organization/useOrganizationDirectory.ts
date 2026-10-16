import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { identityQueryRoots, queryKeys } from "../../api/queryKeys.ts";
import { useOfficeOperation, useOfficePaging } from "../office/useOfficeData.ts";

export function useOrganizationDirectory(client: ExportDocManagerApiClient) {
  return useQuery({ queryKey: queryKeys.organizationDirectory(), queryFn: ({ signal }) => client.getOrganizationDirectory({ signal }) });
}

export function useOrganizationOperation() {
  return useOfficeOperation(identityQueryRoots);
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
