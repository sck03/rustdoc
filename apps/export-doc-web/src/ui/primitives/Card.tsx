import { useEffect, useId, useState, type HTMLAttributes, type ReactNode } from "react";
export type StatusTone = "info" | "positive" | "warning" | "danger" | "muted";
export function Card({ title, children, actions, ...attributes }: Omit<HTMLAttributes<HTMLElement>, "title"> & { title?: string; children: ReactNode; actions?: ReactNode }) {
  return <section {...attributes} data-ui="card">{title && <header><h3>{title}</h3>{actions}</header>}{children}</section>;
}
export function KpiTile({ label, value, description, onActivate, disabled = false }: { label: string; value: ReactNode; description?: ReactNode; onActivate?: () => void; disabled?: boolean }) {
  const id = useId();
  const name = typeof value === "string" || typeof value === "number" ? `${label}：${value}` : undefined;
  const content = <><span id={`${id}-label`}>{label}</span><strong id={`${id}-value`}>{value}</strong>{description && <small id={`${id}-description`}>{description}</small>}</>;
  return onActivate ? <button data-ui="kpi" type="button" aria-label={name} aria-labelledby={name ? undefined : `${id}-label ${id}-value`} aria-describedby={description ? `${id}-description` : undefined} disabled={disabled} onClick={onActivate}>{content}</button> : <article data-ui="kpi">{content}</article>;
}
export function CollapsibleSection({ title, children, defaultOpen = false, error = false, className }: { title: ReactNode; children: ReactNode; defaultOpen?: boolean; error?: boolean; className?: string }) {
  const [open, setOpen] = useState(defaultOpen);
  useEffect(() => { if (error) setOpen(true); }, [error]);
  return <details data-ui="collapsible" className={className} open={open} onToggle={event => setOpen(event.currentTarget.open)} onInvalidCapture={() => setOpen(true)}><summary>{title}</summary><div>{children}</div></details>;
}
export function StatusBadge({ tone = "muted", children, ...attributes }: HTMLAttributes<HTMLSpanElement> & { tone?: StatusTone; children: ReactNode }) {
  return <span {...attributes} data-ui="status-badge" data-tone={tone}>{children}</span>;
}
