import type { RefObject } from "react";
import { useConfirmUnsavedChanges } from "../ui/unsavedChangesGuard.tsx";
import { readApiError } from "../ui/formUtils.ts";
import { isCurrentSession } from "./sessionChannel.ts";
import { revokeSession } from "./sessionApiClient.ts";
import type { WebSessionState } from "./webSessionStorage.ts";
import type { WorkspaceNotice } from "./WorkspaceShell.tsx";

export function useSessionLogout(options: {
  session: RefObject<WebSessionState | null>;
  action: RefObject<symbol | null>;
  unitTransition: RefObject<boolean>;
  desktopAccessToken?: string;
  setBusy: (busy: boolean) => void;
  endSession: (reason: string | null) => void;
  setNotice: (notice: WorkspaceNotice) => void;
}) {
  const confirmDiscardChanges = useConfirmUnsavedChanges();
  return async () => {
    const previous = options.session.current;
    if (!previous || options.action.current || options.unitTransition.current) return;
    if (!await confirmDiscardChanges("退出登录") || !isCurrentSession(options.session.current, previous)
      || options.action.current || options.unitTransition.current) return;
    const action = Symbol();
    options.action.current = action;
    options.setBusy(true);
    const isCurrent = () => options.action.current === action && isCurrentSession(options.session.current, previous);
    try {
      await revokeSession({ baseUrl: previous.apiBaseUrl, accessToken: previous.accessToken, desktopAccessToken: options.desktopAccessToken });
      if (isCurrent()) options.endSession(null);
    } catch (error) {
      if (isCurrent()) options.setNotice({ id: "logout", tone: "warning", title: "退出登录未完成", message: readApiError(error) });
    } finally {
      if (options.action.current === action) {
        options.action.current = null;
        options.setBusy(false);
      }
    }
  };
}
