import { useRef, useState, type CompositionEvent } from "react";
import { ExactDecimal } from "./decimalSum.ts";

/** Keep lexical input (1., 0.05, -) separate from the numeric business value.
 * Valid values reach the parent immediately, including keyboard-triggered saves.
 * External changes (undo, record switch, recalculation) invalidate the buffer.
 */
export function useNumberInputDraft(value: number | undefined, formatted: string, onChange: (value: number | undefined) => void, emptyValue: number | undefined) {
  return useNumericInputDraft(value, formatted, onChange, emptyValue, text => {
    const parsed = Number(text);
    return Number.isFinite(parsed) ? parsed : undefined;
  });
}

export function useDecimalInputDraft(value: string | undefined, formatted: string, onChange: (value: string | undefined) => void, emptyValue: string | undefined = "0") {
  return useNumericInputDraft(value, formatted, onChange, emptyValue, text => {
    if (!/[0-9]/.test(text)) return undefined;
    const normalized = text.replace(/^([+-]?)\./, "$10.").replace(/\.$/, "");
    return ExactDecimal.from(normalized).normalize().toString();
  });
}

function useNumericInputDraft<T extends string | number>(value: T | undefined, formatted: string,
  onChange: (value: T | undefined) => void, emptyValue: T | undefined, parse: (text: string) => T | undefined) {
  const [draft, setDraft] = useState<{ text: string; value: T | undefined } | null>(null);
  const composing = useRef(false);
  // Discard invalidated text permanently so undo/redo cannot resurrect an old buffer.
  if (draft && draft.value !== value) setDraft(null);
  function change(text: string) {
    if (composing.current) { setDraft({ text, value }); return; }
    if (text.length > 60 || !/^[+-]?(?:\d*(?:\.\d*)?)$/.test(text)) return;
    const next = text === "" ? emptyValue : parse(text) ?? value;
    setDraft({ text, value: next });
    if (!Object.is(next, value)) onChange(next);
  }
  return {
    value: draft && draft.value === value ? draft.text : formatted,
    change,
    onCompositionStart() { composing.current = true; },
    onCompositionEnd(event: CompositionEvent<HTMLInputElement>) {
      composing.current = false;
      setDraft(null);
      change(event.currentTarget.value.normalize("NFKC"));
    },
    reset() { composing.current = false; setDraft(null); },
  };
}
