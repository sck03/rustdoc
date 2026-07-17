import { useId, type ReactNode } from "react";

export function PathField({
  label,
  value,
  disabled,
  actions,
  onChange,
}: {
  label: string;
  value?: string;
  disabled?: boolean;
  actions?: ReactNode;
  onChange: (value: string) => void;
}) {
  const labelId = useId();

  return (
    <div className="path-field">
      <span className="path-field-label" id={labelId}>
        {label}
      </span>
      <div className="path-field-control">
        <input
          aria-labelledby={labelId}
          value={value ?? ""}
          disabled={disabled}
          onChange={(event) => onChange(event.target.value)}
        />
        {actions ? <div className="path-field-actions">{actions}</div> : null}
      </div>
    </div>
  );
}

export function PathTextAreaField({
  label,
  value,
  disabled,
  actions,
  onChange,
}: {
  label: string;
  value?: string;
  disabled?: boolean;
  actions?: ReactNode;
  onChange: (value: string) => void;
}) {
  const labelId = useId();

  return (
    <div className="path-field path-textarea-field">
      <span className="path-field-label" id={labelId}>
        {label}
      </span>
      <div className="path-field-control">
        <textarea
          aria-labelledby={labelId}
          value={value ?? ""}
          disabled={disabled}
          onChange={(event) => onChange(event.target.value)}
        />
        {actions ? <div className="path-field-actions">{actions}</div> : null}
      </div>
    </div>
  );
}
