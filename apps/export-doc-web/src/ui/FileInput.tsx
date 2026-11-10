import { useState, type InputHTMLAttributes } from "react";

type Props = Omit<InputHTMLAttributes<HTMLInputElement>, "type" | "value" | "defaultValue" | "children">;

/** Keep the native picker and keyboard semantics; visible labels belong to the app. */
export function FileInput({ onChange, disabled, className, ...props }: Props) {
  const [selection, setSelection] = useState("");
  return <span data-ui="file-input" data-disabled={disabled || undefined} className={className}>
    <span data-file-button aria-hidden="true">选择文件</span>
    <span data-file-selection aria-hidden="true" title={selection}>{selection || "未选择文件"}</span>
    <input title={selection || "选择文件"} {...props} type="file" disabled={disabled} onChange={event => {
      onChange?.(event);
      // Consumers may clear the input immediately to allow choosing the same file again.
      setSelection(Array.from(event.currentTarget.files ?? [], file => file.name).join("、"));
    }} />
  </span>;
}
