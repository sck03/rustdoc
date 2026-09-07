import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, PersonnelTransitionRequest } from "../../api/index.ts";
import { useOfficePaging } from "./useOfficeData.ts";
import type { PersonnelWorkflow } from "./personnelModel.ts";

export function usePersonnelDirectory(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const paging = useOfficePaging();
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const [departmentId, setDepartmentId] = useState("");
  const [status, setStatus] = useState("");
  const [attentionOnly, setAttentionOnly] = useState(false);
  const options = useQuery({ queryKey: ["office", "people", "options", user.id, user.companyScope],
    queryFn: ({ signal }) => client.getPersonnelOptions({ signal }) });
  const query = useQuery({
    queryKey: ["office", "people", "directory", user.id, user.companyScope, search, departmentId, status, attentionOnly, paging.pageNumber, paging.pageSize],
    queryFn: ({ signal }) => client.listPersonnel({ keyword: search || undefined, departmentId: departmentId || undefined,
      status: status || undefined, attentionOnly, pageNumber: paging.pageNumber, pageSize: paging.pageSize }, { signal }),
    refetchInterval: 60000, refetchIntervalInBackground: false,
  });
  return { paging, query, options, keyword, departmentId, status, attentionOnly,
    changeKeyword: (text: string) => { setKeyword(text); if (!text) { setSearch(""); paging.resetPage(); } },
    search: () => { setSearch(keyword.trim()); paging.resetPage(); },
    changeDepartment: (id: string) => { setDepartmentId(id); paging.resetPage(); },
    changeStatus: (next: string) => { setStatus(next); setAttentionOnly(false); paging.resetPage(); },
    changeAttention: (next: boolean) => { setAttentionOnly(next); setStatus(""); paging.resetPage(); },
  };
}

export function usePersonnelRecord(client: ExportDocManagerApiClient, user: ApiUserDto, id: number) {
  return useQuery({ queryKey: ["office", "people", "detail", user.id, user.companyScope, id],
    queryFn: ({ signal }) => client.getPersonnel({ id }, { signal }) });
}

export function usePersonnelClearance(client: ExportDocManagerApiClient, user: ApiUserDto, id: number) {
  return useQuery({ queryKey: ["office", "people", "clearance", user.id, user.companyScope, id],
    queryFn: ({ signal }) => client.getPersonnelClearance({ id }, { signal }), refetchInterval: 15000, refetchIntervalInBackground: false });
}

export function usePersonnelHistory(client: ExportDocManagerApiClient, user: ApiUserDto, id: number) {
  const paging = useOfficePaging();
  const query = useQuery({ queryKey: ["office", "people", "history", user.id, user.companyScope, id, paging.pageNumber, paging.pageSize],
    queryFn: ({ signal }) => client.getPersonnelHistory({ id, pageNumber: paging.pageNumber, pageSize: paging.pageSize }, { signal }) });
  return { paging, query };
}

export function usePersonnelAccountOptions(client: ExportDocManagerApiClient, user: ApiUserDto, id: number) {
  const paging = useOfficePaging();
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const query = useQuery({ queryKey: ["office", "people", "accounts", user.id, user.companyScope, id, search, paging.pageNumber, paging.pageSize],
    queryFn: ({ signal }) => client.listPersonnelAccountOptions({ id, keyword: search || undefined, pageNumber: paging.pageNumber, pageSize: paging.pageSize }, { signal }) });
  return { paging, query, keyword, setKeyword, search: () => { setSearch(keyword.trim()); paging.resetPage(); } };
}

export function applyPersonnelWorkflow(client: ExportDocManagerApiClient, id: number, action: PersonnelWorkflow,
  body: PersonnelTransitionRequest, signal: AbortSignal) {
  const request = { id, body };
  switch (action) {
    case "confirm": return client.confirmPersonnel(request, { signal });
    case "transfer": return client.transferPersonnel(request, { signal });
    case "depart": return client.departPersonnel(request, { signal });
    case "rehire": return client.rehirePersonnel(request, { signal });
  }
}
