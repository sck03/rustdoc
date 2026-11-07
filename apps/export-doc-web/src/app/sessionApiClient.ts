import { ApiError, createExportDocManagerApiClient, type ApiClientOptions } from "../api/index.ts";
import { notifyAuthenticationFailure } from "../api/authenticationFailureEvents.ts";

/** Authentication failures belong to the identity that sent the HTTP request. */
export function createSessionApiClient(options: ApiClientOptions, isCurrent: (requestToken?: string) => boolean) {
  return createExportDocManagerApiClient({
    ...options,
    onAuthenticationFailure: (error, requestToken) => { if (isCurrent(requestToken)) notifyAuthenticationFailure(error); },
  });
}

/** Revocation owns the submitted credentials, independent of the live workspace. */
export async function revokeSession(options: ApiClientOptions & { accessToken: string }) {
  try {
    await createExportDocManagerApiClient(options).logout();
  } catch (error) {
    // An already expired/revoked session needs no further server-side cleanup.
    if (!(error instanceof ApiError && error.status === 401)) throw error;
  }
}
