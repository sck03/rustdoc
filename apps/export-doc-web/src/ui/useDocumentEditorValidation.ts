import { useLayoutEffect, useRef, useState, type FormEvent } from "react";

export function useDocumentEditorValidation<T extends string>(activeSection: T, onNavigate: (section: T) => void, readSection: (value: string | null) => T) {
  const [validationMessage, setValidationMessage] = useState<string | null>(null);
  const invalidField = useRef<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | null>(null);
  useLayoutEffect(() => {
    const field = invalidField.current;
    if (field && !field.closest<HTMLElement>("[role=tabpanel]")?.hidden) {
      field.focus();
      invalidField.current = null;
    }
  }, [activeSection, validationMessage]);
  function revealInvalidField(event: FormEvent<HTMLDivElement>) {
    const field = event.target;
    if (!(field instanceof HTMLInputElement || field instanceof HTMLSelectElement || field instanceof HTMLTextAreaElement)) return;
    event.preventDefault();
    if (invalidField.current) return;
    invalidField.current = field;
    onNavigate(readSection(field.closest<HTMLElement>("[data-editor-section]")?.dataset.editorSection ?? null));
    for (let parent = field.parentElement; parent; parent = parent.parentElement) {
      if (parent instanceof HTMLDetailsElement) parent.open = true;
    }
    setValidationMessage(`${field.closest("label")?.querySelector(".form-field-label")?.textContent?.replace("必填", "") || "字段"}：${field.validationMessage}`);
    if (!field.closest<HTMLElement>("[role=tabpanel]")?.hidden) {
      field.focus();
      invalidField.current = null;
    }
  }
  return { validationMessage, revealInvalidField, clearValidationMessage: () => setValidationMessage(null) };
}
