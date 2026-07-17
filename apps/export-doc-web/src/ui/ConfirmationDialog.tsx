import { useEffect, useId, useRef } from "react";
import { AlertTriangle, X } from "lucide-react";

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

  useEffect(() => {
    cancelButtonRef.current?.focus();

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape" && !isBusy) {
        onCancel();
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
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
          <button className="icon-button" type="button" title="关闭" disabled={isBusy} onClick={onCancel}>
            <X size={18} aria-hidden="true" />
          </button>
        </header>

        {details?.length ? (
          <div className="confirmation-dialog-details">
            {details.map((detail) => (
              <div key={detail}>{detail}</div>
            ))}
          </div>
        ) : null}

        <footer className="confirmation-dialog-footer">
          <button ref={cancelButtonRef} className="command-button secondary" type="button" disabled={isBusy} onClick={onCancel}>
            取消
          </button>
          <button
            className="command-button confirmation-dialog-confirm"
            type="button"
            disabled={isBusy}
            onClick={onConfirm}
          >
            {isBusy ? "处理中…" : confirmLabel}
          </button>
        </footer>
      </div>
    </div>
  );
}
