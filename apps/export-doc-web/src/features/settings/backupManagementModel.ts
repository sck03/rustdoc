import type { ApiBackupCreateResponse, ApiBackupListResponse } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import type { QueryClient } from "@tanstack/react-query";

export function readDesktopError(error: unknown) {
  if (error instanceof Error) {
    return error.message;
  }
  if (typeof error === "string") {
    return error;
  }
  return "桌面运行目录操作失败。";
}

export function isStrongRecoveryPassword(value: string) {
  return value.length >= 12 &&
    value.length <= 128 &&
    /[A-Z]/.test(value) &&
    /[a-z]/.test(value) &&
    /\d/.test(value) &&
    /[^A-Za-z0-9]/.test(value);
}

export function updateBackupQuery(queryClient: QueryClient, response: ApiBackupCreateResponse) {
  queryClient.setQueryData<ApiBackupListResponse>(queryKeys.backups(), {
    backups: response.backups,
    backupRoot: response.backupRoot,
    storagePolicy: response.storagePolicy,
  });
}
