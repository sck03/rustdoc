import { Plus } from "lucide-react";
import { useEffect, useRef } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { FieldShell, SelectField } from "../../ui/FormFields.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useCustomOptionCreation } from "./useCustomOptionCreation.ts";
import "../../styles/custom-option-select.css";

export function CustomOptionSelectField({ client, optionType, label, value, options, disabled = false, maxLength = 100, className = "", onChange }: {
  client: ExportDocManagerApiClient;
  optionType: string;
  label: string;
  value: string;
  options: string[];
  disabled?: boolean;
  maxLength?: number;
  className?: string;
  onChange: (value: string) => void;
}) {
  const creation = useCustomOptionCreation(client, optionType, options, maxLength, onChange);
  const addButton = useRef<HTMLButtonElement>(null);
  const wasAdding = useRef(false);
  useEffect(() => {
    if (wasAdding.current && !creation.isAdding) addButton.current?.focus();
    wasAdding.current = creation.isAdding;
  }, [creation.isAdding]);
  const choices = value && !options.includes(value) ? [...options, value] : options;
  return <div className={`custom-option-select ${className}`}>
    <div className="custom-option-select-controls">
      <SelectField label={label} value={value} options={choices.map((option) => ({ value: option, label: option }))}
        disabled={disabled || creation.busy} onChange={onChange} />
      <Button ref={addButton} disabled={disabled || creation.busy} icon={<Plus size={16} aria-hidden="true" />} onClick={creation.open}>新增选项</Button>
    </div>
    {creation.isAdding && <div className="custom-option-create" data-form-keyboard="native">
      <FieldShell label={`新增${label}`} description="添加后会选中此项，以后填写单据时也可选择。">
        {(descriptionId) => <input autoFocus value={creation.draft} maxLength={maxLength} disabled={disabled || creation.busy}
          aria-describedby={descriptionId} onChange={(event) => creation.setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.nativeEvent.isComposing) return;
            if (event.key === "Enter") { event.preventDefault(); event.stopPropagation(); if (!disabled) void creation.save(); }
            if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); creation.close(); }
          }} />}
      </FieldShell>
      <div className="toolbar-actions">
        <Button variant="primary" disabled={disabled || creation.busy || !creation.draft.trim()} onClick={() => void creation.save()}>
          {creation.busy ? "添加中…" : "添加并选择"}
        </Button>
        <Button disabled={creation.busy} onClick={creation.close}>取消</Button>
      </div>
    </div>}
    {creation.error && <InlineNotice tone="error" title="选项未添加">{creation.error}</InlineNotice>}
    {creation.message && <p className="form-field-description" role="status">{creation.message}</p>}
  </div>;
}
