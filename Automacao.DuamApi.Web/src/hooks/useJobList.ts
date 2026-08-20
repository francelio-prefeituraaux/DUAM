import { useQuery } from "@tanstack/react-query";
import { listJobs } from "../api/duamApi";
import type { JobListFilters } from "../types/job";

export function useJobList(filters: JobListFilters) {
  return useQuery({
    queryKey: ["jobs", filters],
    queryFn: () => listJobs(filters),
    placeholderData: (previousData) => previousData,
  });
}
