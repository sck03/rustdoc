import { Children, cloneElement, isValidElement, useId, type AriaAttributes, type FormEventHandler, type ReactNode } from "react";

export function Field({ label, children, wide = false, inline = false, required = false, error, description, className }: {
  label: string; children: ReactNode | ((descriptionId?: string) => ReactNode); wide?: boolean; inline?: boolean; required?: boolean; error?: string; description?: string; className?: string;
}) {
  const hintId = useId();
  const content = typeof children === "function" ? children(error || description ? hintId : undefined) : children;
  const mandatory = required || Children.toArray(content).some(child => isValidElement<{required?:boolean}>(child) && child.props.required);
  const controls = Children.map(content, child => {
    if (!isValidElement<AriaAttributes>(child)
      || typeof child.type !== "string" || !["input", "select", "textarea"].includes(child.type)) return child;
    const describedBy = error || description
      ? [...new Set([...(child.props["aria-describedby"] ?? "").split(/\s+/).filter(Boolean), hintId])].join(" ")
      : child.props["aria-describedby"];
    return cloneElement(child, {
      "aria-label": child.props["aria-label"] ?? (child.props["aria-labelledby"] ? undefined : label),
      "aria-describedby": describedBy,
      "aria-invalid": error ? true : child.props["aria-invalid"],
      "aria-required": mandatory || child.props["aria-required"] || undefined,
    });
  });
  return <label className={className} data-ui="field" data-wide={wide || undefined} data-inline={inline || undefined} data-invalid={Boolean(error) || undefined}>
    <span className="form-field-label"><span>{label}</span>{mandatory && <small data-ui="required" aria-hidden="true">必填</small>}</span>{controls}
    {error ? <small id={hintId} role="alert">{error}</small> : description ? <small id={hintId} className="form-field-description">{description}</small> : null}
  </label>;
}
export function FormGrid({ children, columns = 2, disabled, className }: { children: ReactNode; columns?: 1 | 2 | 3; disabled?: boolean; className?: string }) {
  const props = { className, "data-ui": "form-grid", "data-columns": columns };
  return disabled === undefined ? <div {...props}>{children}</div> : <fieldset {...props} disabled={disabled}>{children}</fieldset>;
}
export function FilterBar({ children, label = "筛选条件", className, onSubmit }: { children: ReactNode; label?: string; className?: string; onSubmit?: FormEventHandler<HTMLFormElement> }) {
  const props = { className, "data-ui": "filter-bar", "aria-label": label };
  return onSubmit ? <form {...props} role="search" onSubmit={onSubmit}>{children}</form> : <div {...props} role="group">{children}</div>;
}
