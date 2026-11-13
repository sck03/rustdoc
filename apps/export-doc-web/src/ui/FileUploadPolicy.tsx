import { createContext, useContext, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../api/index.ts";
import { queryKeys } from "../api/queryKeys.ts";

const FilePolicyClient = createContext<ExportDocManagerApiClient | null>(null);
export function FileUploadPolicyProvider({ client, children }: { client: ExportDocManagerApiClient; children: ReactNode }) {
  return <FilePolicyClient value={client}>{children}</FilePolicyClient>;
}
export function useFileUploadPolicy(enabled: boolean) {
  const client = useContext(FilePolicyClient);
  return useQuery({ queryKey: queryKeys.fileStoragePolicy(), queryFn: ({ signal }) => {
    if (!client) throw new Error("文件上传服务未配置。");
    return client.getFileStoragePolicy({ signal });
  }, enabled, staleTime: 30_000 });
}
