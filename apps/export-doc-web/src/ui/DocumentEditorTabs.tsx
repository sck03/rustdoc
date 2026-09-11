import { useEffect, useState, type KeyboardEvent, type ReactNode } from "react";
import { Save } from "lucide-react";
import { Button } from "./Button.tsx";
import "../styles/business/document-editor-navigation.css";

export type DocumentEditorTab<T extends string> = { id: T; label: string; description: string; badge?: number };

export function DocumentEditorNavigation<T extends string>({ prefix, label, title, summary, tabs, activeSection, busy, editable, saving, isNew, hasUnsavedChanges, onNavigate, action }: {
  prefix: string; label: string; title: string; summary: string; tabs: readonly DocumentEditorTab<T>[];
  activeSection: T; busy: boolean; editable: boolean; saving: boolean; isNew: boolean; hasUnsavedChanges: boolean;
  onNavigate: (section: T) => void; action?: ReactNode;
}) {
  function moveTab(event: KeyboardEvent<HTMLDivElement>) {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
    const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
    const current = buttons.indexOf(event.target as HTMLButtonElement);
    if (current < 0) return;
    event.preventDefault();
    const index = event.key === "Home" ? 0 : event.key === "End" ? buttons.length - 1 : (current + (event.key === "ArrowLeft" ? -1 : 1) + buttons.length) % buttons.length;
    onNavigate(tabs[index].id);
    buttons[index].focus();
  }
  return <div id={`${prefix}-editor-navigation`} className="document-editor-navigation">
    <div className="document-editor-sticky-actions" role="region" aria-label={`${label}保存操作`}>
      <div><strong>{title}</strong><span>{summary} · {saving ? "保存中…" : hasUnsavedChanges ? "有未保存修改" : isNew ? "尚未保存" : "已保存"}</span></div>
      <Button variant="primary" type="submit" disabled={busy || !editable} icon={<Save size={17} aria-hidden="true" />}>{saving ? "保存中" : `保存${label}`}</Button>
    </div>
    <div className="document-editor-section-nav">
      <div className="document-editor-tabs" role="tablist" aria-label={`${label}编辑分区`} onKeyDown={moveTab}>
        {tabs.map((tab) => <button key={tab.id} type="button" role="tab" id={`${prefix}-tab-${tab.id}`}
          aria-controls={`${prefix}-${tab.id}-section`} aria-selected={activeSection === tab.id} tabIndex={activeSection === tab.id ? 0 : -1}
          className="document-section-nav-item" onClick={() => onNavigate(tab.id)}>
          <span>{tab.label}</span>{tab.badge !== undefined && <small>{tab.badge}</small>}
        </button>)}
      </div>{action}
    </div>
    <p className="document-editor-section-help">{tabs.find((tab) => tab.id === activeSection)?.description}</p>
  </div>;
}

export function DocumentEditorTabPanel({ prefix, id, activeSection, eager = false, children }: {
  prefix: string; id: string; activeSection: string; eager?: boolean; children: ReactNode;
}) {
  const active = id === activeSection;
  const [visited, setVisited] = useState(active || eager);
  useEffect(() => { if (active) setVisited(true); }, [active]);
  return <div id={`${prefix}-${id}-section`} data-editor-section={id} className="document-editor-tab-panel"
    role="tabpanel" aria-labelledby={`${prefix}-tab-${id}`} hidden={!active} tabIndex={0}>
    {active || visited ? children : null}
  </div>;
}
