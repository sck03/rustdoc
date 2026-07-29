export function getClipboardPasteInstruction(target = "表格或输入区域") {
  return `请先聚焦${target}，然后按 Ctrl+V（macOS 使用 Cmd+V）。`;
}

export async function readClipboardText() {
  if (!window.isSecureContext || !navigator.clipboard?.readText) {
    return null;
  }

  try {
    return await navigator.clipboard.readText();
  } catch {
    return null;
  }
}

export async function writeClipboardText(text: string) {
  try {
    if (window.isSecureContext && navigator.clipboard?.writeText) {
      await Promise.race([
        navigator.clipboard.writeText(text),
        new Promise<never>((_, reject) => {
          window.setTimeout(() => reject(new Error("clipboard-timeout")), 800);
        }),
      ]);
      return true;
    }
  } catch {
    // Use the selection-based browser fallback below.
  }

  let textArea: HTMLTextAreaElement | null = null;
  try {
    textArea = document.createElement("textarea");
    textArea.value = text;
    textArea.setAttribute("readonly", "readonly");
    textArea.style.position = "fixed";
    textArea.style.left = "-9999px";
    textArea.style.top = "0";
    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();
    return document.execCommand("copy");
  } catch {
    return false;
  } finally {
    textArea?.remove();
  }
}
