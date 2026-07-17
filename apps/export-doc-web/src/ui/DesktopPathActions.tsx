import { type ReactNode } from "react";
import { ExternalLink } from "lucide-react";
import { isDesktopBridgeAvailable, openPath } from "../desktop/desktopBridge.ts";

export function DesktopIconButton({
  title,
  disabled,
  children,
  onClick,
}: {
  title: string;
  disabled?: boolean;
  children: ReactNode;
  onClick: () => void | Promise<void>;
}) {
  return (
    <button
      className="icon-button"
      type="button"
      title={title}
      aria-label={title}
      disabled={disabled}
      onClick={() => void onClick()}
    >
      {children}
    </button>
  );
}

export function renderOpenPathAction(path: string | undefined, title: string, onError: (message: string) => void) {
  if (!isDesktopBridgeAvailable()) {
    return undefined;
  }

  return (
    <DesktopIconButton title={title} disabled={!path?.trim()} onClick={() => openDesktopPath(path ?? "", onError)}>
      <ExternalLink size={15} aria-hidden="true" />
    </DesktopIconButton>
  );
}

export function readDesktopError(error: unknown) {
  if (error instanceof Error) {
    return error.message;
  }

  return typeof error === "string" && error.trim() ? error : "桌面路径操作失败。";
}

async function openDesktopPath(path: string, onError: (message: string) => void) {
  try {
    await openPath(path);
  } catch (error) {
    onError(readDesktopError(error));
  }
}
