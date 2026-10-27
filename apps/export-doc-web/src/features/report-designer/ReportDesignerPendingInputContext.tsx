import { createContext, useContext, useId, useLayoutEffect } from "react";

export const ReportDesignerPendingInputContext = createContext<((id: string, pending: boolean) => void) | null>(null);

/** Buffered property inputs protect the document until their one-step commit. */
export function useReportDesignerPendingInput(pending: boolean) {
  const id = useId();
  const register = useContext(ReportDesignerPendingInputContext);
  useLayoutEffect(() => {
    register?.(id, pending);
    return () => register?.(id, false);
  }, [id, pending, register]);
}
