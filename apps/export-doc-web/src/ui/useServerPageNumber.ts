import { useEffect } from "react";

/** Wait for the current request; placeholders and refetching cache entries may describe an older page. */
export function useServerPageNumber(
  query: { data?: { pageNumber: number }; isPlaceholderData: boolean; isSuccess: boolean; isFetching: boolean },
  pageNumber: number,
  setPageNumber: (page: number) => void,
) {
  const serverPage = query.isSuccess && !query.isPlaceholderData && !query.isFetching ? query.data?.pageNumber : undefined;
  useEffect(() => {
    if (serverPage !== undefined && serverPage !== pageNumber) setPageNumber(serverPage);
  }, [serverPage, pageNumber, setPageNumber]);
}
