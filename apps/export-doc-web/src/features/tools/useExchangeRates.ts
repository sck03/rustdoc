import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useAbortableOperation, isAbortError } from "../../ui/useAbortableOperation.ts";

export function useExchangeRates(client: ExportDocManagerApiClient) {
  const cache = useQueryClient();
  const settings = useQuery({
    queryKey: queryKeys.settings(), queryFn: ({ signal }) => client.getSettings({ signal }), staleTime: 30_000,
  });
  const configuration = settings.data?.settings.exchangeRate;
  const scope = JSON.stringify(configuration);
  const key = queryKeys.exchangeRates(scope);
  const run = useAbortableOperation(scope);
  const query = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => client.listExchangeRates({}, { signal }),
    enabled: Boolean(configuration),
    staleTime: 30_000,
    retry: false,
    refetchInterval: query => (query.state.data?.autoRefreshEnabled ?? configuration?.autoRefreshEnabled) ? 60_000 : false,
    refetchIntervalInBackground: false,
  });
  const refresh = useMutation({
    mutationFn: () => run(async signal => {
      await cache.cancelQueries({ queryKey: key });
      signal.throwIfAborted();
      return client.listExchangeRates({ forceRefresh: true }, { signal });
    }),
    onSuccess: result => cache.setQueryData(key, result),
  });
  const resetRefresh = refresh.reset;
  useEffect(() => resetRefresh(), [scope, resetRefresh]);
  return {
    configuration,
    result: query.data,
    isBusy: settings.isFetching || query.isFetching || refresh.isPending,
    error: settings.error || query.error || (isAbortError(refresh.error) ? null : refresh.error),
    refresh: () => {
      if (!configuration || settings.isError) { void settings.refetch(); return; }
      refresh.reset(); refresh.mutate();
    },
  };
}
