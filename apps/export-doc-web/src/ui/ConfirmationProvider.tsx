import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from "react";
import { ConfirmationDialog } from "./ConfirmationDialog.tsx";

export type ConfirmationRequest = {
  title: string;
  description: string;
  details?: string[];
  confirmLabel?: string;
  tone?: "danger" | "warning";
};

type PendingConfirmation = ConfirmationRequest & {
  resolve: (confirmed: boolean) => void;
};

const ConfirmationContext = createContext<((request: ConfirmationRequest) => Promise<boolean>) | null>(null);

export function ConfirmationProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<PendingConfirmation | null>(null);
  const pendingRef = useRef<PendingConfirmation | null>(null);

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

export function useConfirmation() {
  const confirm = useContext(ConfirmationContext);
  if (!confirm) {
    throw new Error("useConfirmation must be used inside ConfirmationProvider.");
  }
  return confirm;
}
