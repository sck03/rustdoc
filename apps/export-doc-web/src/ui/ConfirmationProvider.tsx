import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { ConfirmationDialog } from "./ConfirmationDialog.tsx";
import { ConfirmationContext, type ConfirmationRequest } from "./confirmationContext.ts";

type PendingConfirmation = ConfirmationRequest & {
  resolve: (confirmed: boolean) => void;
};

export function ConfirmationProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<PendingConfirmation | null>(null);
  const pendingRef = useRef<PendingConfirmation | null>(null);
  useEffect(() => () => {
    pendingRef.current?.resolve(false);
    pendingRef.current = null;
  }, []);

  const confirm = useCallback((request: ConfirmationRequest) => {
    pendingRef.current?.resolve(false);
    return new Promise<boolean>((resolve) => {
      const next = { ...request, resolve };
      pendingRef.current = next;
      setPending(next);
    });
  }, []);

  const settle = useCallback((confirmed: boolean) => {
    const current = pendingRef.current;
    pendingRef.current = null;
    setPending(null);
    current?.resolve(confirmed);
  }, []);

  return (
    <ConfirmationContext.Provider value={confirm}>
      {children}
      {pending ? (
        <ConfirmationDialog
          title={pending.title}
          description={pending.description}
          details={pending.details}
          confirmLabel={pending.confirmLabel ?? "确认"}
          tone={pending.tone ?? "warning"}
          onCancel={() => settle(false)}
          onConfirm={() => settle(true)}
        />
      ) : null}
    </ConfirmationContext.Provider>
  );
}
