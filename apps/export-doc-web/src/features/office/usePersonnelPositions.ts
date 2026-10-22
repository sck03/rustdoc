import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";

export function usePersonnelPositions(client: ExportDocManagerApiClient, user: ApiUserDto, employeeId?: number) {
  return useQuery({ queryKey: ["office", "people", "positions", user.id, user.companyScope, employeeId], staleTime: 60_000,
    queryFn: ({ signal }) => client.getPersonnelOptions({ employeeId }, { signal }) });
}
