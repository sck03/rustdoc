import { useEffect, useId, useState, type ReactNode, type RefObject } from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";
import { useModalDialog } from "../useModalDialog.ts";
import { InlineNotice } from "../PageState.tsx";
import { useUnsavedChangesGuard } from "../unsavedChangesGuard.tsx";

function DialogSurface({ title, description, children, footer, onClose, closeLabel = "关闭窗口", purpose = "content", busy = false, error = "",
  wide = false, size, initialFocusRef, variant = "dialog", onDirty }: {
  title: string; description?: string; children: ReactNode; footer?: ReactNode; onClose: () => void; closeLabel?: string; purpose?: "content" | "confirmation";
  busy?: boolean; error?: string; protectChanges?: boolean; hasChanges?: boolean; wide?: boolean;
  size?: "small" | "medium" | "large" | "full"; initialFocusRef?: RefObject<HTMLElement | null>; variant?: "dialog" | "drawer"; onDirty?: () => void;
}) {
  const titleId = useId(); const descriptionId = useId();
  function close() { if (!busy) onClose(); }
  const ref = useModalDialog(close, { canClose: !busy, initialFocusRef });
  useEffect(() => { if (error) ref.current?.querySelectorAll("details").forEach(section => { section.open = true; }); }, [error, ref]);
  return createPortal(<div data-ui="dialog-backdrop" data-variant={variant} onSubmit={event => event.stopPropagation()}
    onMouseDown={event => { if(event.target === event.currentTarget) close(); }}>
    <div data-ui="dialog" data-dialog-purpose={purpose} data-size={size ?? (wide ? "large" : "medium")} data-variant={variant} ref={ref}
      role="dialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={description ? descriptionId : undefined}>
      <header data-ui="dialog-header"><div><h2 id={titleId}>{title}</h2>{description && <p id={descriptionId}>{description}</p>}</div>
        <button type="button" className="icon-button" aria-label={closeLabel} disabled={busy} onClick={close}><X size={18} aria-hidden="true" /></button>
      </header>
      <div data-ui="dialog-body" onChangeCapture={event => { if (!(event.target as HTMLElement).closest('[data-dialog-control]')) onDirty?.(); }}
        onInvalidCapture={event => { let section=(event.target as HTMLElement).closest("details");while(section){section.open=true;section=section.parentElement?.closest("details")??null;} }}>
        {error && <InlineNotice tone="error" title="操作未完成">{error}</InlineNotice>}{children}
      </div>
      {footer && <footer data-ui="dialog-footer">{footer}</footer>}
    </div>
  </div>, document.body);
}
type DialogProps = Parameters<typeof DialogSurface>[0];
function ProtectedDialog(props: DialogProps) {
  const [dirty,setDirty] = useState(false);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty: dirty || Boolean(props.hasChanges), message: `${props.title}有尚未保存的内容。` });
  const close = () => { if(!props.busy) void confirmDiscardChanges("关闭窗口").then(accepted => { if(accepted) props.onClose(); }); };
  return <DialogSurface {...props} onClose={close} onDirty={() => setDirty(true)} />;
}
export function Dialog(props: DialogProps) { return props.protectChanges ? <ProtectedDialog {...props} /> : <DialogSurface {...props} />; }
export function Drawer(props: Omit<Parameters<typeof Dialog>[0], "variant">) { return <Dialog {...props} variant="drawer" />; }
