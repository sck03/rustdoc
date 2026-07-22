import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";

export type ButtonVariant = "primary" | "secondary" | "danger" | "text";

export const Button = forwardRef<HTMLButtonElement, ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  icon?: ReactNode;
}>(function Button({
  variant = "secondary",
  icon,
  children,
  className = "",
  ...props
}, ref) {
  const variantClass = variant === "primary" ? "command-button"
    : variant === "danger" ? "command-button secondary danger"
      : variant === "text" ? "text-button"
        : "command-button secondary";
  return <button ref={ref} className={`${variantClass} ${className}`.trim()} type="button" {...props}>
    {icon}
    {children ? <span>{children}</span> : null}
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
