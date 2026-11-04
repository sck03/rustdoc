import { useId, type ReactNode, type KeyboardEvent } from "react";

export type TabItem<T extends string> = { key: T; label: ReactNode; disabled?: boolean; panelId?: string };
export function Tabs<T extends string>({ items, value, onChange, label, variant = "tabs", children, idPrefix }: {
  items: readonly TabItem<T>[]; value: T; onChange: (value: T) => void;
  label: string; variant?: "tabs" | "segmented"; children?: ReactNode; idPrefix?: string;
}) {
  const generatedId = useId();
  const id = idPrefix ?? generatedId;
  function move(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const enabled = items.map((item, index) => item.disabled ? -1 : index).filter(index => index >= 0);
    const position = enabled.indexOf(index);
    let next: number | undefined;
    if (event.key === "Home") next = enabled[0];
    if (event.key === "End") next = enabled.at(-1);
    if (event.key === "ArrowRight") next = enabled[(position + 1) % enabled.length];
    if (event.key === "ArrowLeft") next = enabled[(position + enabled.length - 1) % enabled.length];
    if (next === undefined) return;
    event.preventDefault();
    onChange(items[next].key);
    event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus();
  }
  return <>
    <div data-ui="tabs" data-variant={variant} role="tablist" aria-label={label}>
      {items.map((item, index) => <button key={item.key} id={getTabId(id, item.key)} type="button" role="tab"
        disabled={item.disabled} aria-selected={value === item.key} tabIndex={value === item.key ? 0 : -1}
        aria-controls={item.panelId ?? (children === undefined ? undefined : `${id}-panel`)}
        onClick={() => onChange(item.key)} onKeyDown={event => move(event, index)}>{item.label}</button>)}
    </div>
    {children !== undefined && <div id={`${id}-panel`} role="tabpanel" aria-labelledby={getTabId(id, value)} data-ui="tab-panel">{children}</div>}
  </>;
}
export function getTabId(idPrefix: string, key: string) { return `${idPrefix}-tab-${key}`; }
export function SegmentedNav<T extends string>(props: Omit<Parameters<typeof Tabs<T>>[0], "variant">) {
  return <Tabs {...props} variant="segmented" />;
}
