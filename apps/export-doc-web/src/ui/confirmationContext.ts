import { createContext, useContext } from "react";

export type ConfirmationRequest = {
  title: string;
  description: string;
  details?: string[];
  confirmLabel?: string;
  tone?: "danger" | "warning";
};

export const ConfirmationContext = createContext<((request: ConfirmationRequest) => Promise<boolean>) | null>(null);

export function useConfirmation() {
  const confirm = useContext(ConfirmationContext);
  if (!confirm) throw new Error("useConfirmation must be used inside ConfirmationProvider.");
  return confirm;
}
