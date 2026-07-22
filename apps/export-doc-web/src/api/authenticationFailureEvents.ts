import { ApiError } from "./index.ts";

type AuthenticationFailureListener = () => void;

const listeners = new Set<AuthenticationFailureListener>();
let notificationScheduled = false;

export function subscribeToAuthenticationFailure(listener: AuthenticationFailureListener) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function notifyAuthenticationFailure(error: unknown) {
  if (!(error instanceof ApiError) || error.status !== 401 || notificationScheduled) {
    return false;
  }

  notificationScheduled = true;
  queueMicrotask(() => {
    notificationScheduled = false;
    for (const listener of listeners) {
      listener();
    }
  });
  return true;
}
