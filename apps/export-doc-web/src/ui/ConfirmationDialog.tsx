import { useId, useRef } from "react";
import { AlertTriangle, X } from "lucide-react";
import { Button, IconButton } from "./Button.tsx";
import { useModalDialog } from "./useModalDialog.ts";

export function ConfirmationDialog({
  title,
  description,
  details,
  confirmLabel,
  isBusy = false,
  tone = "danger",
  onCancel,
  onConfirm,
}: {
  title: string;
  description: string;
  details?: string[];
  confirmLabel: string;
  isBusy?: boolean;
  tone?: "danger" | "warning";
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const titleId = useId();
  const descriptionId = useId();
  const cancelButtonRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useModalDialog<HTMLDivElement>(onCancel, {
    canClose: !isBusy,
    initialFocusRef: cancelButtonRef,
  });

  return (
    <div
      className="confirmation-dialog-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !isBusy) {
          onCancel();
        }
      }}
    >
      <div
        ref={dialogRef}
        className={`confirmation-dialog confirmation-dialog-${tone}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
      >
        <header className="confirmation-dialog-header">
          <span className="confirmation-dialog-icon" aria-hidden="true">
            <AlertTriangle size={20} />
          </span>
          <div>
            <h2 id={titleId}>{title}</h2>
            <p id={descriptionId}>{description}</p>
          </div>
          <IconButton label="关闭确认窗口" disabled={isBusy} onClick={onCancel}>
            <X size={18} aria-hidden="true" />
          </IconButton>
        </header>

        {details?.length ? (
          <div className="confirmation-dialog-details">
            {details.map((detail) => (
              <div key={detail}>{detail}</div>
            ))}
          </div>
        ) : null}

        <footer className="confirmation-dialog-footer">
          <Button ref={cancelButtonRef} variant="secondary" disabled={isBusy} onClick={onCancel}>取消</Button>
          <Button
            variant="primary"
            className="confirmation-dialog-confirm"
            disabled={isBusy}
            onClick={onConfirm}
          >
            {isBusy ? "处理中…" : confirmLabel}
          </Button>
        </footer>
      </div>
    </div>
  );
}
