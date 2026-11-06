import { useRef, useState, type CompositionEvent } from "react";

/** Keep lexical input (1., 0.05, -) separate from the numeric business value.
 * Valid values reach the parent immediately, including keyboard-triggered saves.
 * External changes (undo, record switch, recalculation) invalidate the buffer.
 */
export function useNumberInputDraft(value: number | undefined, formatted: string, onChange: (value: number | undefined) => void, emptyValue: number | undefined) {
  const [draft, setDraft] = useState<{ text: string; value: number | undefined } | null>(null);
  const composing = useRef(false);
  // Discard invalidated text permanently so undo/redo cannot resurrect an old buffer.
  if (draft && draft.value !== value) setDraft(null);
  function change(text: string) {
    if (composing.current) { setDraft({ text, value }); return; }
    if (!/^-?(?:\d*(?:\.\d*)?)$/.test(text)) return;
    const parsed = text === "" ? emptyValue : Number(text);
    const next = parsed === undefined || Number.isFinite(parsed) ? parsed : value;
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
