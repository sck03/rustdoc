import { type ReactNode, useId } from "react";
import { dateInputToApiDate, numberInputValue, readNumber, toDateInputValue } from "./formUtils.ts";

type FieldShellProps = {
  label: string;
  required?: boolean;
  disabled?: boolean;
  className?: string;
  description?: string;
  children: (descriptionId?: string) => ReactNode;
};

export function FieldShell({
  label,
  required,
  disabled,
  className,
  description,
  children,
}: FieldShellProps) {
  const descriptionId = `field-description-${useId().replace(/:/g, "-")}`;
  const classes = [
    "form-field",
    required ? "form-field-required" : "",
    disabled ? "form-field-disabled" : "",
    className ?? "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <label className={classes}>
      <span className="form-field-label">
        <span>{label}</span>
        {required ? <strong className="form-field-required-badge">必填</strong> : null}
      </span>
      {children(description ? descriptionId : undefined)}
      {description ? (
        <span id={descriptionId} className="form-field-description">
          {description}
        </span>
      ) : null}
    </label>
  );
}

export function TextField({
  label,
  value,
  required,
  disabled,
  className,
  description,
  placeholder,
  autoComplete,
  onChange,
}: {
  label: string;
  value?: string;
  required?: boolean;
  disabled?: boolean;
  className?: string;
  description?: string;
  placeholder?: string;
  autoComplete?: string;
  onChange: (value: string) => void;
}) {
  return (
    <FieldShell label={label} required={required} disabled={disabled} className={className} description={description}>
      {(descriptionId) => (
      <input
        value={value ?? ""}
        required={required}
        disabled={disabled}
        placeholder={placeholder}
        autoComplete={autoComplete}
        aria-describedby={descriptionId}
        onChange={(event) => onChange(event.target.value)}
      />
      )}
    </FieldShell>
  );
}

export function SelectField({
  label,
  value,
  disabled,
  className,
  description,
  includeEmptyOption = true,
  options,
  onChange,
}: {
  label: string;
  value: string;
  disabled?: boolean;
  className?: string;
  description?: string;
  includeEmptyOption?: boolean;
  options: Array<{ value: string; label: string }>;
  onChange: (value: string) => void;
}) {
  return (
    <FieldShell label={label} disabled={disabled} className={className} description={description}>
      {(descriptionId) => (
      <select value={value} disabled={disabled} aria-describedby={descriptionId} onChange={(event) => onChange(event.target.value)}>
        {includeEmptyOption ? <option value="">未选择</option> : null}
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label || "-"}
          </option>
        ))}
      </select>
      )}
    </FieldShell>
  );
}

export function EditableComboField({
  label,
  value,
  required,
  disabled,
  className,
  description,
  placeholder,
  options,
  transformValue,
  onChange,
  onCommit,
}: {
  label: string;
  value?: string;
  required?: boolean;
  disabled?: boolean;
  className?: string;
  description?: string;
  placeholder?: string;
  options?: string[];
  transformValue?: (value: string) => string;
  onChange: (value: string) => void;
  onCommit?: (value: string) => void;
}) {
  const listId = `combo-${useId().replace(/:/g, "-")}`;
  const normalizedOptions = Array.from(
    new Set((options ?? []).map((option) => option.trim()).filter(Boolean)),
  );

  function commitCurrentValue() {
    if (disabled) {
      return;
    }

    const normalized = (value ?? "").trim();
    if (normalized) {
      onCommit?.(normalized);
    }
  }

  return (
    <FieldShell label={label} required={required} disabled={disabled} className={className} description={description}>
      {(descriptionId) => (
      <>
      <input
        list={normalizedOptions.length > 0 ? listId : undefined}
        value={value ?? ""}
        required={required}
        disabled={disabled}
        placeholder={placeholder}
        aria-describedby={descriptionId}
        onBlur={commitCurrentValue}
        onChange={(event) => onChange(transformValue ? transformValue(event.target.value) : event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            commitCurrentValue();
            event.currentTarget.blur();
          }
        }}
      />
      {normalizedOptions.length > 0 ? (
        <datalist id={listId}>
          {normalizedOptions.map((option) => (
          <option key={option} value={option} />
        ))}
      </datalist>
      ) : null}
      </>
      )}
    </FieldShell>
  );
}

export function DateField({
  label,
  value,
  required,
  disabled,
  className,
  description,
  onChange,
}: {
  label: string;
  value?: string;
  required?: boolean;
  disabled?: boolean;
  className?: string;
  description?: string;
  onChange: (value: string) => void;
}) {
  return (
    <FieldShell label={label} required={required} disabled={disabled} className={className} description={description}>
      {(descriptionId) => (
      <input
        type="date"
        value={toDateInputValue(value)}
        required={required}
        disabled={disabled}
        aria-describedby={descriptionId}
        onChange={(event) => onChange(event.target.value ? dateInputToApiDate(event.target.value) : "")}
      />
      )}
    </FieldShell>
  );
}

export function NumberField({
  label,
  value,
  required,
  disabled,
  className,
  description,
  step = "0.01",
  onChange,
}: {
  label: string;
  value?: number;
  required?: boolean;
  disabled?: boolean;
  className?: string;
  description?: string;
  step?: string;
  onChange: (value: number) => void;
}) {
  return (
    <FieldShell label={label} required={required} disabled={disabled} className={className} description={description}>
      {(descriptionId) => (
      <input
        type="number"
        step={step}
        value={numberInputValue(value)}
        required={required}
        disabled={disabled}
        aria-describedby={descriptionId}
        onChange={(event) => onChange(readNumber(event.target.value))}
      />
      )}
    </FieldShell>
  );
}

export function TextAreaField({
  label,
  value,
  required,
  disabled,
  className,
  description,
  onChange,
}: {
  label: string;
  value?: string;
  required?: boolean;
  disabled?: boolean;
  className?: string;
  description?: string;
  onChange: (value: string) => void;
}) {
  return (
    <FieldShell
      label={label}
      required={required}
      disabled={disabled}
      className={className ? `textarea-field ${className}` : "textarea-field"}
      description={description}
    >
      {(descriptionId) => (
      <textarea
        value={value ?? ""}
        required={required}
        disabled={disabled}
        aria-describedby={descriptionId}
        onChange={(event) => onChange(event.target.value)}
      />
      )}
    </FieldShell>
  );
}
