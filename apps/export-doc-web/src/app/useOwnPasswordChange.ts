import type { RefObject } from "react";
import type { ExportDocManagerApiClient } from "../api/index.ts";
import { useConfirmUnsavedChanges } from "../ui/unsavedChangesGuard.tsx";
import { isCurrentSession } from "./sessionChannel.ts";
import type { WebSessionState } from "./webSessionStorage.ts";
import type { ChangeOwnPassword } from "./AccountMenu.tsx";

export function useOwnPasswordChange(options: {
  client: ExportDocManagerApiClient; session: RefObject<WebSessionState | null>; action: RefObject<symbol | null>;
  unitTransition: RefObject<boolean>; setBusy: (busy: boolean) => void; endSession: (reason: string | null) => void;
}): ChangeOwnPassword {
  const confirmDiscardChanges = useConfirmUnsavedChanges();
  return async (body, signal) => {
    const previous = options.session.current;
    if (!previous || options.action.current || options.unitTransition.current) return false;
    if (!await confirmDiscardChanges("修改密码并重新登录") || !isCurrentSession(options.session.current, previous)
      || options.action.current || options.unitTransition.current) return false;
    signal.throwIfAborted();
    const action = Symbol();
    options.action.current = action; options.setBusy(true);
    const isCurrent = () => options.action.current === action && isCurrentSession(options.session.current, previous);
    try {
      await options.client.changeOwnPassword({ body }, { signal });
      if (!isCurrent()) return false;
      options.endSession("密码修改成功，请使用新密码重新登录。");
      return true;
    } catch (error) {
      if (isCurrent()) throw error;
      return false;
    } finally {
      if (options.action.current === action) { options.action.current = null; options.setBusy(false); }
    }
  };
}
