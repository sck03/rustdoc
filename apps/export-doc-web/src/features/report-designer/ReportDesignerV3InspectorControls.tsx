import { useEffect, useRef, useState } from "react";
import { ensureCurrentSelectOption, type SelectOption } from "./ReportDesignerPropertyControls.tsx";
import { formatNumber } from "./reportDesignerV3WorkspaceHelpers.tsx";

export function SelectField({ label, value, options, disabled = false, className, onChange }: { label: string; value: string; options: SelectOption[]; disabled?: boolean; className?: string; onChange: (value: string) => void }) {
  const safeOptions = ensureCurrentSelectOption(options, value);
  const hasUnknownValue = Boolean(value) && !options.some((option) => option.value === value);
  return <label className={className}><span>{label}</span><select aria-invalid={hasUnknownValue ? true : undefined} value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>{safeOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label>;
}

export function InspectorTitle({ title, subtitle }: { title: string; subtitle: string }) {
  return <div className="report-designer-v3-inspector-title"><strong>{title}</strong><span>{subtitle}</span></div>;
}

export function NumberField({ label, value, onCommit, min = 0, max = 1000, disabled = false }: { label: string; value: number; onCommit: (value: number) => void; min?: number; max?: number; disabled?: boolean }) {
  const [draft, setDraft] = useState(formatNumber(value));
  const [focused, setFocused] = useState(false);
  const cancelOnBlur = useRef(false);
  useEffect(() => setDraft(formatNumber(value)), [value]);
  function commit() {
    if (cancelOnBlur.current) {
      cancelOnBlur.current = false;
      setDraft(formatNumber(value));
      return;
    }
    if (!draft.trim()) return setDraft(formatNumber(value));
    const parsed = Number(draft);
    if (!Number.isFinite(parsed)) return setDraft(formatNumber(value));
    const next = Math.min(max, Math.max(min, parsed));
    setDraft(formatNumber(next));
    if (next !== value) onCommit(next);
  }
  return <label><span>{label}</span><input type="number" inputMode="decimal" step="0.01" min={min} max={max} disabled={disabled} value={focused ? draft : formatNumber(value)} onFocus={() => { setFocused(true); setDraft(formatNumber(value)); }} onChange={(event) => setDraft(event.target.value)} onBlur={() => { setFocused(false); commit(); }} onKeyDown={(event) => { if (event.key === "Escape") { event.preventDefault(); cancelOnBlur.current = true; setDraft(formatNumber(value)); event.currentTarget.blur(); } else if (event.key === "Enter") { event.preventDefault(); event.currentTarget.blur(); } }} /></label>;
}

export function focusDesignerNode(selector: string) {
  requestAnimationFrame(() => document.querySelector<HTMLElement>(selector)?.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" }));
}
