import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";

export const hsImportChanges = { Add: "新增", Update: "更新", Unchanged: "不变", SuspectedObsolete: "疑似作废", Conflict: "冲突", Invalid: "无效" } as const;
export type HsImportChange = keyof typeof hsImportChanges;

export function useHsImportPreview(client: ExportDocManagerApiClient, token: string) {
  const [filter, setFilter] = useState<HsImportChange | "">("");
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(50);
  const query = useQuery({
    queryKey: ["hs-import-preview", token, filter, page, size],
    queryFn: ({ signal }) => client.listHsCodeImportPreviewItems({ token, changeType: filter || undefined, pageNumber: page, pageSize: size }, { signal }),
    gcTime: 0,
  });
  useServerPageNumber(query, page, setPage);
  return { query, filter, page, size, setPage,
    setFilter: (value: HsImportChange | "") => { setFilter(value); setPage(1); },
    setSize: (value: number) => { setSize(value); setPage(1); },
  };
}
