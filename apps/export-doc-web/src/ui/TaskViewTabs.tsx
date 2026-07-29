import { useId, useRef, type KeyboardEvent } from "react";

export type TaskViewTab<T extends string> = {
  id: T;
  label: string;
  disabled?: boolean;
};

export function TaskViewTabs<T extends string>({
  value,
  items,
  onChange,
  label,
  idPrefix,
}: {
  value: T;
  items: readonly TaskViewTab<T>[];
  onChange: (value: T) => void;
  label: string;
  idPrefix?: string;
}) {
  const buttonRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const generatedId = useId().replace(/:/g, "");
  const resolvedIdPrefix = idPrefix?.trim() || `task-view-${generatedId}`;

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const enabledIndexes = items.map((item, itemIndex) => item.disabled ? -1 : itemIndex).filter((itemIndex) => itemIndex >= 0);
    if (!enabledIndexes.length) return;
    const enabledPosition = Math.max(enabledIndexes.indexOf(index), 0);
    const nextPosition = event.key === "Home" ? 0
      : event.key === "End" ? enabledIndexes.length - 1
        : event.key === "ArrowRight" ? (enabledPosition + 1) % enabledIndexes.length
          : (enabledPosition - 1 + enabledIndexes.length) % enabledIndexes.length;
    const nextIndex = enabledIndexes[nextPosition];
    buttonRefs.current[nextIndex]?.focus();
    onChange(items[nextIndex].id);
  }

  return <nav className="task-view-tabs" role="tablist" aria-label={label}>
    {items.map((item, index) => {
      const active = value === item.id;
      return <button key={item.id} className={active ? "task-view-tab task-view-tab-active" : "task-view-tab"}
        id={getTaskViewTabId(resolvedIdPrefix, item.id)} aria-controls={getTaskViewPanelId(resolvedIdPrefix, item.id)}
        type="button" role="tab" disabled={item.disabled} aria-selected={active} tabIndex={active ? 0 : -1}
        ref={(element) => { buttonRefs.current[index] = element; }} onKeyDown={(event) => handleKeyDown(event, index)}
        onClick={() => onChange(item.id)}>{item.label}</button>;
    })}
  </nav>;
}

export function getTaskViewTabId(idPrefix: string, tabId: string) {
  return `${idPrefix}-tab-${tabId}`;
}

export function getTaskViewPanelId(idPrefix: string, tabId: string) {
  return `${idPrefix}-panel-${tabId}`;
}

export function getTaskViewPanelProps(idPrefix: string, tabId: string) {
  return {
    id: getTaskViewPanelId(idPrefix, tabId),
    role: "tabpanel" as const,
    "aria-labelledby": getTaskViewTabId(idPrefix, tabId),
  };
}
