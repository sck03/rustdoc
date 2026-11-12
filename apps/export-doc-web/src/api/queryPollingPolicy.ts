import { shouldRetryQueryFailure } from "./queryRetryPolicy.ts";

type PollingQuery = { state: { error: unknown; fetchFailureCount: number } };

/** Permanent failures need an explicit retry; transient failures keep bounded recovery. */
export function queryPollingInterval({ state }: PollingQuery): number | false {
  if (!state.error) return 30_000;
  if (!shouldRetryQueryFailure(0, state.error)) return false;
  return Math.min(30_000 * 2 ** Math.max(1, state.fetchFailureCount), 300_000);
}

export const officePolling = {
  refetchInterval: queryPollingInterval,
  refetchIntervalInBackground: false,
};

const terminalRequestStatuses = new Set(["Completed", "Cancelled", "Rejected", "Terminated", "HandedOff"]);

export function oaRequestPollingInterval(query: PollingQuery & { state: { data?: { status: string } } }) {
  return query.state.data && terminalRequestStatuses.has(query.state.data.status) ? false : queryPollingInterval(query);
}
