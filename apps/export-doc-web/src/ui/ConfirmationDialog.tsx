import { useEffect, useId, useRef } from "react";
import { AlertTriangle, X } from "lucide-react";
import { Button, IconButton } from "./Button.tsx";

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
  const dialogRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const previouslyFocusedElement = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    cancelButtonRef.current?.focus();

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape" && !isBusy) {
        event.preventDefault();
        onCancel();
        return;
      }

      if (event.key !== "Tab") {
        return;
      }

      const focusableElements = Array.from(
        dialogRef.current?.querySelectorAll<HTMLElement>(
          'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
        ) ?? [],
      );
      if (focusableElements.length === 0) {
        event.preventDefault();
        return;
      }

      const first = focusableElements[0];
      const last = focusableElements[focusableElements.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
      previouslyFocusedElement?.focus();
    };
  }, [isBusy, onCancel]);

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
