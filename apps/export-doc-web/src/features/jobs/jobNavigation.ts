import type { NavigateFunction, NavigateOptions } from "react-router-dom";

export function normalizeJobId(value: string | null | undefined) {
  return (value ?? "").trim();
}

export function buildJobCenterPath(jobId: string | null | undefined) {
  const normalized = normalizeJobId(jobId);
  if (!normalized) {
    return "/jobs";
  }

  return `/jobs?jobId=${encodeURIComponent(normalized)}`;
}

export function navigateToJobCenter(
  navigate: NavigateFunction,
  jobId: string | null | undefined,
  options?: NavigateOptions,
) {
  navigate(buildJobCenterPath(jobId), options);
}
