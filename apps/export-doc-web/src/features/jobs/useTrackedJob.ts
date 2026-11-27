import { useEffect, useState } from "react";
import { useQuery, useQueryClient, type QueryKey } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { queryPollingInterval } from "../../api/queryPollingPolicy.ts";
import { isTerminalJob } from "./jobPresentation.ts";

/** Losing a receipt never retries the write. The user can retry the read or
 * leave tracking and inspect the receipt in the job center. */
export function useTrackedJob(client: ExportDocManagerApiClient, enabled: boolean, affected: readonly QueryKey[]) {
  const cache = useQueryClient();
  const [tracked, setTracked] = useState<{ client: ExportDocManagerApiClient; id: string } | null>(null);
  const jobId = enabled && tracked?.client === client ? tracked.id : "";
  useEffect(() => { setTracked(null); }, [client, enabled]);
  const job = useQuery({
    queryKey: queryKeys.job(jobId), enabled: Boolean(jobId),
    queryFn: ({ signal }) => client.getJob({ jobId }, { signal }),
    refetchInterval: query => isTerminalJob(query.state.data?.status) ? false : queryPollingInterval(query, 2000),
    refetchIntervalInBackground: false,
  });
  useEffect(() => {
    if (jobId && isTerminalJob(job.data?.status)) {
      for (const queryKey of affected) void cache.invalidateQueries({ queryKey });
    }
  }, [jobId, job.data?.status, affected, cache]);
  return {
    jobId, job, active: Boolean(jobId && !isTerminalJob(job.data?.status)),
    uncertain: Boolean(jobId && job.isError),
    track: (id: string) => setTracked({ client, id }),
    retry: () => { void job.refetch(); },
    dismiss: () => { setTracked(null); },
  };
}
