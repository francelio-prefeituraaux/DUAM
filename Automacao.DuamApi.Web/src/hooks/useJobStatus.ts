import { useQuery } from "@tanstack/react-query";
import { getJob } from "../api/duamApi";
import { TERMINAL_STATUSES } from "../types/job";

const POLL_INTERVAL_MS = 3000;

export function useJobStatus(jobId: string | undefined) {
  return useQuery({
    queryKey: ["job", jobId],
    queryFn: () => getJob(jobId!),
    enabled: Boolean(jobId),
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      if (!status || TERMINAL_STATUSES.includes(status)) return false;
      return POLL_INTERVAL_MS;
    },
  });
}
