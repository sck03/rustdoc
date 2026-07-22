import { QueryClient } from "@tanstack/react-query";
import { queryRetryDelay, shouldRetryQueryFailure } from "./api/queryRetryPolicy.ts";

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      gcTime: 5 * 60 * 1000,
      networkMode: "online",
      refetchOnReconnect: true,
      refetchOnWindowFocus: false,
      retry: shouldRetryQueryFailure,
      retryDelay: queryRetryDelay,
      staleTime: 30 * 1000,
    },
    mutations: {
      networkMode: "online",
      retry: false,
    },
  },
});
