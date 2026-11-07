import { useEffect, useId, useState, type ReactNode } from "react";
import { getTabId, Tabs } from "./primitives/Tabs.tsx";

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
  const generatedId = useId().replace(/:/g, "");
  const resolvedIdPrefix = idPrefix?.trim() || `task-view-${generatedId}`;
  return <Tabs label={label} idPrefix={resolvedIdPrefix} value={value} onChange={onChange} variant="segmented"
    items={items.map(item => ({ key: item.id, label: item.label, disabled: item.disabled, panelId: getTaskViewPanelId(resolvedIdPrefix, item.id) }))} />;
}

function getTaskViewTabId(idPrefix: string, tabId: string) {
  return getTabId(idPrefix, tabId);
}

function getTaskViewPanelId(idPrefix: string, tabId: string) {
  return `${idPrefix}-panel-${tabId}`;
}

export function getTaskViewPanelProps(idPrefix: string, tabId: string) {
  return {
    id: getTaskViewPanelId(idPrefix, tabId),
    role: "tabpanel" as const,
    "data-ui": "tab-panel",
    "aria-labelledby": getTaskViewTabId(idPrefix, tabId),
  };
}

export function TaskViewPanel({ idPrefix, id, active, children }: {
  idPrefix: string; id: string; active: boolean; children: ReactNode;
}) {
  const [visited, setVisited] = useState(active);
  useEffect(() => { if (active) setVisited(true); }, [active]);
  return <div {...getTaskViewPanelProps(idPrefix, id)} hidden={!active} tabIndex={0}>
    {active || visited ? children : null}
  </div>;
}
