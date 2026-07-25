import { useEffect, useId, useRef } from "react";
import { AlertTriangle, X } from "lucide-react";
import { Button, IconButton } from "../../ui/Button.tsx";

export function InvoiceStatusReasonDialog({
  title,
  description,
  value,
  isBusy = false,
  onChange,
  onCancel,
  onConfirm,
}: {
  title: string;
  description: string;
  value: string;
  isBusy?: boolean;
  onChange: (value: string) => void;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const titleId = useId();
  const descriptionId = useId();
  const reasonId = useId();
  const reasonRef = useRef<HTMLTextAreaElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const onCancelRef = useRef(onCancel);

  useEffect(() => {
    onCancelRef.current = onCancel;
  }, [onCancel]);

  useEffect(() => {
    const previouslyFocusedElement = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    reasonRef.current?.focus();

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape" && !isBusy) {
        event.preventDefault();
        onCancelRef.current();
        return;
      }

      if (event.key !== "Tab") {
        return;
      }

      const focusable = Array.from(
        dialogRef.current?.querySelectorAll<HTMLElement>(
          'button:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
        ) ?? [],
      );
      if (focusable.length === 0) {
        event.preventDefault();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
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
  }, [isBusy]);

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
        className="confirmation-dialog confirmation-dialog-danger invoice-status-reason-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
      >
        <header className="confirmation-dialog-header">
          <span className="confirmation-dialog-icon" aria-hidden="true"><AlertTriangle size={20} /></span>
          <div>
            <h2 id={titleId}>{title}</h2>
            <p id={descriptionId}>{description}</p>
          </div>
          <IconButton label="关闭原因窗口" disabled={isBusy} onClick={onCancel}><X size={18} aria-hidden="true" /></IconButton>
        </header>
        <label className="invoice-status-reason-field" htmlFor={reasonId}>
          <span>作废原因 <strong>必填</strong></span>
          <textarea
            ref={reasonRef}
            id={reasonId}
            value={value}
            maxLength={500}
            disabled={isBusy}
            placeholder="例如：订单取消、资料作废或重复建单"
            onChange={(event) => onChange(event.target.value)}
          />
          <small>{value.length}/500</small>
        </label>
        <footer className="confirmation-dialog-footer">
          <Button variant="secondary" disabled={isBusy} onClick={onCancel}>取消</Button>
          <Button
            variant="primary"
            className="confirmation-dialog-confirm"
            disabled={isBusy || !value.trim()}
            onClick={onConfirm}
          >
            {isBusy ? "处理中…" : "继续作废"}
          </Button>
        </footer>
      </div>
    </div>
  );
}
