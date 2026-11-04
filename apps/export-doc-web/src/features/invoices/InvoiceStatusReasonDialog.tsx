import { useRef } from "react";
import { Button } from "../../ui/Button.tsx";
import { Dialog } from "../../ui/primitives/Dialog.tsx";
import { Field } from "../../ui/primitives/Field.tsx";

export function InvoiceStatusReasonDialog({ title, description, value, isBusy = false, onChange, onCancel, onConfirm }: {
  title: string; description: string; value: string; isBusy?: boolean;
  onChange: (value: string) => void; onCancel: () => void; onConfirm: () => void;
}) {
  const reasonRef = useRef<HTMLTextAreaElement>(null);
  return <Dialog title={title} description={description} onClose={onCancel} closeLabel="关闭原因窗口"
    purpose="confirmation" busy={isBusy} initialFocusRef={reasonRef}
    footer={<><Button variant="secondary" disabled={isBusy} onClick={onCancel}>取消</Button>
      <Button data-dialog-action="confirm" variant="danger" disabled={isBusy || !value.trim()} onClick={onConfirm}>{isBusy ? "处理中…" : "继续作废"}</Button></>}>
    <Field label="作废原因" required className="invoice-status-reason-field" description={`${value.length}/500`}>
      <textarea ref={reasonRef} value={value} maxLength={500} disabled={isBusy} placeholder="例如：订单取消、资料作废或重复建单" onChange={event => onChange(event.target.value)} />
    </Field>
  </Dialog>;
}
