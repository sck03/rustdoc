import { useEffect, useRef, useState } from "react";
import { ensureCurrentSelectOption, type SelectOption } from "./ReportDesignerPropertyControls.tsx";
import { formatNumber } from "./reportDesignerV3WorkspaceHelpers.tsx";
import { useReportDesignerPendingInput } from "./ReportDesignerPendingInputContext.tsx";

export function SelectField({ label, value, options, disabled = false, className, onChange }: { label: string; value: string; options: SelectOption[]; disabled?: boolean; className?: string; onChange: (value: string) => void }) {
  const safeOptions = ensureCurrentSelectOption(options, value);
  const hasUnknownValue = Boolean(value) && !options.some((option) => option.value === value);
  return <label className={className}><span>{label}</span><select aria-invalid={hasUnknownValue ? true : undefined} value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>{safeOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label>;
}

export function InspectorTitle({ title, subtitle }: { title: string; subtitle: string }) {
  return <div className="report-designer-v3-inspector-title"><strong>{title}</strong><span>{subtitle}</span></div>;
}

type NumberFieldProps = { label: string; min?: number; max?: number; step?: number; precision?: number; disabled?: boolean } & (
  { value: number; onCommit: (value: number) => void; optional?: false } |
  { value: number | undefined; onCommit: (value: number | undefined) => void; optional: true }
);

export function NumberField(props: NumberFieldProps) {
  const { label, value, onCommit, min = 0, max = 1000, step = 0.01, precision = 2, disabled = false } = props;
  const formatted = value === undefined ? "" : formatNumber(value, precision);
  const [draft, setDraft] = useState(formatted);
  const [focused, setFocused] = useState(false);
  useReportDesignerPendingInput(focused && draft !== formatted);
  const cancelOnBlur = useRef(false);
  useEffect(() => setDraft(formatted), [formatted]);
  function commit() {
    if (cancelOnBlur.current) {
      cancelOnBlur.current = false;
      setDraft(formatted);
      return;
    }
    if (draft === formatted) return;
    if (!draft.trim()) {
      if (props.optional) { setDraft(""); props.onCommit(undefined); }
      else setDraft(formatted);
      return;
    }
    const parsed = Number(draft);
    if (!Number.isFinite(parsed)) return setDraft(formatted);
    const next = Math.min(max, Math.max(min, step === 1 ? Math.round(parsed) : parsed));
    setDraft(formatNumber(next, precision));
    if (next !== value) onCommit(next);
  }
  // Native validation sees the displayed precision; the model retains exact bounds.
  const displayMin = Number(formatNumber(min, precision));
  const displayMax = Number(formatNumber(max, precision));
  return <label><span>{label}</span><input type="number" inputMode={step === 1 ? "numeric" : "decimal"} placeholder={props.optional ? "自动" : undefined} step={step === 1 ? 1 : "any"} min={displayMin} max={displayMax} disabled={disabled} value={focused ? draft : formatted} onFocus={() => { if (!focused) setDraft(formatted); setFocused(true); }} onChange={(event) => setDraft(event.target.value)} onBlur={() => { if (!disabled) { setFocused(false); commit(); } }} onKeyDown={(event) => { if (event.nativeEvent.isComposing) return; if (event.key === "Escape") { event.preventDefault(); cancelOnBlur.current = true; setDraft(formatted); event.currentTarget.blur(); } else if (event.key === "Enter") { event.preventDefault(); event.currentTarget.blur(); } }} /></label>;
}

export function focusDesignerNode(selector: string) {
  requestAnimationFrame(() => {
    const node = document.querySelector<HTMLElement>(selector);
    const viewport = node?.closest<HTMLElement>(".report-designer-v3-canvas-scroll");
    if (!node || !viewport || !node.getClientRects().length) return;
    const rect = node.getBoundingClientRect(), bounds = viewport.getBoundingClientRect();
    viewport.scrollTo({
      top: viewport.scrollTop + rect.top - bounds.top - (viewport.clientHeight - rect.height) / 2,
      left: viewport.scrollLeft + rect.left - bounds.left - (viewport.clientWidth - rect.width) / 2,
      behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth",
    });
  });
}
