import { useRef, type ReactNode } from "react";
import { Button } from "./Button.tsx";
import { Dialog } from "./primitives/Dialog.tsx";

export function ConfirmationDialog({ title, description, details, confirmLabel, isBusy = false, tone = "danger", onCancel, onConfirm, children, confirmDisabled = false }: {
  title: string; description: string; details?: string[]; confirmLabel: string; isBusy?: boolean; tone?: "danger" | "warning";
  onCancel: () => void; onConfirm: () => void; children?: ReactNode; confirmDisabled?: boolean;
}) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  return <Dialog title={title} description={description} onClose={onCancel} closeLabel="取消确认" purpose="confirmation" busy={isBusy} size="small" initialFocusRef={cancelRef}
    footer={<><Button ref={cancelRef} variant="secondary" disabled={isBusy} onClick={onCancel}>取消</Button>
      <Button data-dialog-action="confirm" variant={tone === "danger" ? "danger" : "primary"} disabled={isBusy || confirmDisabled} onClick={onConfirm}>{isBusy ? "处理中…" : confirmLabel}</Button></>}>
    {details?.length ? <ul>{details.map(detail => <li key={detail}>{detail}</li>)}</ul> : null}{children}
  </Dialog>;
}
