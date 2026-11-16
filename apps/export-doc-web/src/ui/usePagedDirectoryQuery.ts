import { keepPreviousData, useQuery, type QueryKey } from "@tanstack/react-query";

export function usePagedDirectoryQuery<TData>(
  queryKey: QueryKey,
  query: (signal: AbortSignal) => Promise<TData>,
  enabled = true,
  retention: { gcTime?: number } = {},
) {
  return useQuery({
    ...retention,
    queryKey,
    queryFn: ({ signal }) => query(signal),
    placeholderData: keepPreviousData,
    enabled,
  });
}
