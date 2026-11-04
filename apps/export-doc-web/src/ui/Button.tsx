import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";
import { LoaderCircle } from "lucide-react";

export type ButtonVariant = "primary" | "secondary" | "danger" | "text";

export const Button = forwardRef<HTMLButtonElement, ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  icon?: ReactNode;
  busy?: boolean;
}>(function Button({
  variant = "secondary",
  icon,
  busy,
  disabled,
  children,
  className = "",
  ...props
}, ref) {
  const variantClass = variant === "primary" ? "command-button"
    : variant === "danger" ? "command-button secondary danger"
      : variant === "text" ? "text-button"
        : "command-button secondary";
  return <button ref={ref} data-ui="button" data-variant={variant} className={`${variantClass} ${className}`.trim()} type="button" {...props}
    disabled={disabled || busy} aria-busy={busy || props["aria-busy"] || undefined}>
    {busy === undefined ? icon : <span data-ui="button-indicator" aria-hidden="true">{busy ? <LoaderCircle size={16} /> : icon}</span>}
    {children === undefined || children === null ? null : <span>{children}</span>}
  </button>;
});

export function IconButton({
  label,
  children,
  className = "",
  ...props
}: Omit<ButtonHTMLAttributes<HTMLButtonElement>, "aria-label" | "title"> & {
  label: string;
  children: ReactNode;
}) {
  return <button
    className={`icon-button ${className}`.trim()}
    type="button"
    aria-label={label}
    title={label}
    {...props}
  >
    {children}
  </button>;
}
