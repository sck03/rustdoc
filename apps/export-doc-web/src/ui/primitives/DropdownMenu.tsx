import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { isComposingKey } from "../keyboardEvent.ts";
import "./dropdown-menu.css";

type MenuItem = { key: string; label: string; icon?: ReactNode; onSelect: () => void; disabled?: boolean; danger?: boolean; separator?: boolean };

export function DropdownMenu({ label, trigger, triggerClassName, items, disabled = false }: {
  label: string; trigger: ReactNode; triggerClassName?: string; items: MenuItem[]; disabled?: boolean;
}) {
  const id = useId();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const initialIndex = useRef(0);
  const close = (restoreFocus = false) => { setOpen(false); if (restoreFocus) button.current?.focus(); };
  const controls = () => Array.from(menu.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)') ?? []);
  useEffect(() => {
    if (!open) return;
    const focus = window.requestAnimationFrame(() => {
      const buttons = controls();
      buttons[initialIndex.current < 0 ? buttons.length - 1 : 0]?.focus();
    });
    const dismiss = (event: PointerEvent) => { if (!root.current?.contains(event.target as Node)) setOpen(false); };
    document.addEventListener("pointerdown", dismiss);
    return () => { window.cancelAnimationFrame(focus); document.removeEventListener("pointerdown", dismiss); };
  }, [open]);
  useEffect(() => { if (disabled) setOpen(false); }, [disabled]);
  return <div className="dropdown-menu" ref={root} onBlur={event => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) close();
  }}>
    <button id={`${id}-trigger`} ref={button} type="button" className={triggerClassName}
      aria-haspopup="menu" aria-expanded={open} aria-controls={open ? `${id}-menu` : undefined} disabled={disabled}
      onClick={() => { initialIndex.current = 0; setOpen(value => !value); }}
      onKeyDown={event => {
        if (isComposingKey(event.nativeEvent)) return;
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
          event.preventDefault(); initialIndex.current = event.key === "ArrowUp" ? -1 : 0; setOpen(true);
        }
      }}><span className="visually-hidden">{label}</span>{trigger}</button>
    {open && <div id={`${id}-menu`} ref={menu} role="menu" aria-labelledby={`${id}-trigger`} className="dropdown-menu-panel"
      onKeyDown={event => {
        if (isComposingKey(event.nativeEvent)) return;
        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(true); return; }
        if (event.key === "Tab") { close(true); return; }
        if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
        event.preventDefault();
        const buttons = controls();
        const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
        const next = event.key === "Home" ? 0 : event.key === "End" ? buttons.length - 1
          : (index + (event.key === "ArrowDown" ? 1 : -1) + buttons.length) % buttons.length;
        buttons[next]?.focus();
      }}>
      {items.map(item => <button key={item.key} type="button" role="menuitem" className="dropdown-menu-item"
        data-danger={item.danger || undefined} data-separator={item.separator || undefined} disabled={item.disabled}
        onClick={() => { close(true); item.onSelect(); }}>{item.icon}<span>{item.label}</span></button>)}
    </div>}
  </div>;
}
