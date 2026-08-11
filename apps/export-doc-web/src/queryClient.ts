import { MutationCache, QueryCache, QueryClient } from "@tanstack/react-query";
import { notifyAuthenticationFailure } from "./api/authenticationFailureEvents.ts";
import { resolveQueryNetworkMode } from "./api/queryNetworkMode.ts";
import { queryRetryDelay, shouldRetryQueryFailure } from "./api/queryRetryPolicy.ts";
import { isDesktopBridgeAvailable } from "./desktop/desktopBridge.ts";

const networkMode = resolveQueryNetworkMode(isDesktopBridgeAvailable());

export const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: notifyAuthenticationFailure,
  }),
  mutationCache: new MutationCache({
    onError: notifyAuthenticationFailure,
  }),
  defaultOptions: {
    queries: {
      gcTime: 5 * 60 * 1000,
      networkMode,
      refetchOnReconnect: true,
      refetchOnWindowFocus: false,
      retry: shouldRetryQueryFailure,
      retryDelay: queryRetryDelay,
      staleTime: 30 * 1000,
    },
    mutations: {
      networkMode,
      retry: false,
    },
  },
});
