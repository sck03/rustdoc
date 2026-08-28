import type { QueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../api/index.ts";
import { queryKeys } from "../api/queryKeys.ts";

/**
 * Prefetch the dashboard query for the destination route in parallel with lazy
 * chunk download and route navigation, so the landing page renders with data
 * already cached. Only fires for actual dashboard destinations; other routes
 * (payments, query, about, denied) skip the unnecessary network request.
 */
export function prefetchLandingDashboard(options: {
  queryClient: QueryClient;
  client: ExportDocManagerApiClient;
  route: string;
}): void {
  const { queryClient, client, route } = options;
  if (route === "/crm/dashboard") {
    void queryClient.prefetchQuery({
      queryKey: queryKeys.crmDashboard(),
      queryFn: ({ signal }) => client.getCrmDashboard({ signal }),
    });
  } else if (route === "/dashboard") {
    void queryClient.prefetchQuery({
      queryKey: queryKeys.dashboard(),
      queryFn: ({ signal }) => client.getDashboard({ signal }),
    });
  }
}
