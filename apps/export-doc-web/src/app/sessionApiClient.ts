import { createExportDocManagerApiClient, type ApiClientOptions } from "../api/index.ts";
import { notifyAuthenticationFailure } from "../api/authenticationFailureEvents.ts";

/** Authentication failures belong to the identity that sent the HTTP request. */
export function createSessionApiClient(options: ApiClientOptions, isCurrent: () => boolean) {
  return createExportDocManagerApiClient({
    ...options,
    onAuthenticationFailure: error => { if (isCurrent()) notifyAuthenticationFailure(error); },
  });
}
