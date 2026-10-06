import { useEffect } from "react";

/** Wait for the current request; placeholders and refetching cache entries may describe an older page. */
export function useServerPageNumber(
  query: {
    data?: { pageNumber: number; totalCount?: number; pageSize?: number };
    isPlaceholderData: boolean; isSuccess: boolean; fetchStatus: "idle" | "fetching" | "paused";
    isFetchedAfterMount: boolean; isEnabled: boolean; refetch: () => Promise<unknown>;
  },
  pageNumber: number,
  setPageNumber: (page: number) => void,
) {
  const data = query.isSuccess && !query.isPlaceholderData && query.fetchStatus === "idle" ? query.data : undefined;
  // Some contracts return the requested page even when it is now out of range.
  const lastPage = data?.totalCount !== undefined && data.pageSize && data.pageSize > 0
    ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : Infinity;
  const serverPage = data ? Math.min(data.pageNumber, lastPage) : undefined;
  const { isEnabled, isFetchedAfterMount, refetch } = query;
  useEffect(() => {
    if (!isEnabled || serverPage === undefined || serverPage === pageNumber) return;
    // Fresh cache can still describe an earlier correction. Verify it once for
    // this query before moving away from the page the user just requested.
    if (!isFetchedAfterMount) void refetch();
    else setPageNumber(serverPage);
  }, [isEnabled, isFetchedAfterMount, refetch, serverPage, pageNumber, setPageNumber]);
}
