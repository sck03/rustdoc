import { type Dispatch, type SetStateAction, useCallback, useEffect, useRef } from "react";
import type { QueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../api/index.ts";
import { notifyAuthenticationFailure } from "../api/authenticationFailureEvents.ts";
import { calculateBusinessDateRefreshDelay } from "../api/businessDateRefreshModel.ts";
import { type WebSessionState, writeStoredSession } from "./webSessionStorage.ts";

type BusinessDateSessionRefreshOptions = {
  client: ExportDocManagerApiClient;
  desktopContextLoading: boolean;
  queryClient: QueryClient;
  session: WebSessionState | null;
  sessionRef: { current: WebSessionState | null };
  setSession: Dispatch<SetStateAction<WebSessionState | null>>;
};

export function useBusinessDateSessionRefresh({
  client,
  desktopContextLoading,
  queryClient,
  session,
  sessionRef,
  setSession,
}: BusinessDateSessionRefreshOptions) {
  const activeRefreshRef = useRef<Promise<void> | null>(null);
  const skipInitialRefreshRef = useRef(true);
  const accessToken = session?.accessToken;

  const refreshCurrentUser = useCallback(() => {
    if (!accessToken || desktopContextLoading) {
      return Promise.resolve();
    }
    if (activeRefreshRef.current) {
      return activeRefreshRef.current;
    }

    const refresh = client.getCurrentUser()
      .then((user) => {
        const currentSession = sessionRef.current;
        if (!currentSession || currentSession.accessToken !== accessToken) {
          return;
        }

        if (JSON.stringify(currentSession.user.capabilities) !== JSON.stringify(user.capabilities)) {
          queryClient.clear();
        }
        const nextSession = { ...currentSession, user };
        setSession(nextSession);
        writeStoredSession(nextSession);
      })
      .catch((error) => {
        notifyAuthenticationFailure(error);
      })
      .finally(() => {
        if (activeRefreshRef.current === refresh) {
          activeRefreshRef.current = null;
        }
      });
    activeRefreshRef.current = refresh;
    return refresh;
  }, [accessToken, client, desktopContextLoading, queryClient, sessionRef, setSession]);

  useEffect(() => {
    // The login response and desktop context already include the user object,
    // so skip the immediate refresh on first mount to avoid a redundant
    // /api/auth/me round-trip that delays the initial dashboard render.
    // Subsequent mount changes (token rotation, reconnection) still refresh.
    if (skipInitialRefreshRef.current) {
      skipInitialRefreshRef.current = false;
      return;
    }
    void refreshCurrentUser();
  }, [refreshCurrentUser]);

  useEffect(() => {
    if (!accessToken || desktopContextLoading) {
      return undefined;
    }

    const delay = calculateBusinessDateRefreshDelay(session?.user.businessDateValidUntilUtc);
    const timerId = delay === null
      ? undefined
      : window.setTimeout(() => void refreshCurrentUser(), delay);
    const refreshWhenVisible = () => {
      if (document.visibilityState === "visible") {
        void refreshCurrentUser();
      }
    };
    const refreshWhenOnline = () => void refreshCurrentUser();
    document.addEventListener("visibilitychange", refreshWhenVisible);
    window.addEventListener("online", refreshWhenOnline);

    return () => {
      if (timerId !== undefined) {
        window.clearTimeout(timerId);
      }
      document.removeEventListener("visibilitychange", refreshWhenVisible);
      window.removeEventListener("online", refreshWhenOnline);
    };
  }, [accessToken, desktopContextLoading, refreshCurrentUser, session?.user.businessDateValidUntilUtc]);
}
