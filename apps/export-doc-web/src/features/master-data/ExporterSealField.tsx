import { FolderOpen, Upload } from "lucide-react";
import { useRef } from "react";
import { isDesktopBridgeAvailable, selectExporterSealImageFile } from "../../desktop/desktopBridge.ts";
import { PathField } from "../../ui/PathField.tsx";

export type ExporterSealType = "document" | "customs";

export function ExporterSealField({
  label,
  value,
  inputDisabled,
  actionDisabled,
  actionTitle,
  onPathChange,
  onPathSelected,
  onUploadFile,
  onError,
}: {
  label: string;
  value: string;
  inputDisabled?: boolean;
  actionDisabled?: boolean;
  actionTitle?: string;
  onPathChange?: (value: string) => void;
  onPathSelected: (path: string) => void;
  onUploadFile: (file: File) => void;
  onError: (error: unknown) => void;
}) {
  const uploadInputRef = useRef<HTMLInputElement>(null);
  const desktopAvailable = isDesktopBridgeAvailable();
  const sealLabel = label.replace(/路径$/, "");
  const title = actionTitle || (desktopAvailable ? `选择${sealLabel}图片路径` : `上传${sealLabel}图片`);

  async function chooseSealImage() {
    if (actionDisabled) return;

    if (!desktopAvailable) {
      uploadInputRef.current?.click();
      return;
    }

    try {
      const selectedPath = await selectExporterSealImageFile();
      if (selectedPath) onPathSelected(selectedPath);
    } catch (error) {
      onError(error);
    }
  }

  return (
    <>
      <PathField
        disabled={inputDisabled}
        label={label}
        value={value}
        onChange={onPathChange ?? (() => undefined)}
        actions={
          <button
            className="icon-button compact-icon-button"
            type="button"
            title={title}
            aria-label={title}
            disabled={actionDisabled}
            onClick={() => void chooseSealImage()}
          >
            {desktopAvailable ? <FolderOpen size={15} aria-hidden="true" /> : <Upload size={15} aria-hidden="true" />}
          </button>
        }
      />
      <input
        ref={uploadInputRef}
        hidden
        type="file"
        accept="image/png,image/jpeg,image/gif,image/webp,.png,.jpg,.jpeg,.gif,.webp"
        disabled={actionDisabled}
        onChange={(event) => {
          const file = event.currentTarget.files?.[0];
          event.currentTarget.value = "";
          if (file) onUploadFile(file);
        }}
      />
    </>
  );
}
