import { useQuery } from "@tanstack/react-query";
import type {ApiUserDto,ExportDocManagerApiClient} from "../../api/index.ts";
import {oaAccess} from "./oaModel.ts";
export function useOaApplications(client:ExportDocManagerApiClient,user:ApiUserDto,enabled=true) {
  return useQuery({queryKey:["office","oa-applications",user.id,user.companyScope,user.contextId],enabled:enabled&&oaAccess(user,"general","view"),queryFn:({signal})=>client.listOaApplications({signal}),staleTime:30000});
}
