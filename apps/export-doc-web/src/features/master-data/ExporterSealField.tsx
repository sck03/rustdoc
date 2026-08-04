import { FolderOpen, Upload } from "lucide-react";
import { useRef } from "react";
import {
  isDesktopBridgeAvailable,
  readExporterSealImageFileAsDataUrl,
  selectExporterSealImageFile,
} from "../../desktop/desktopBridge.ts";
import { PathField } from "../../ui/PathField.tsx";

export type ExporterSealType = "document" | "customs";

export function ExporterSealField({
  label,
  value,
  inputDisabled,
  actionDisabled,
  actionTitle,
  onPathChange,
  onUploadFile,
  onError,
}: {
  label: string;
  value: string;
  inputDisabled?: boolean;
  actionDisabled?: boolean;
  actionTitle?: string;
  onPathChange?: (value: string) => void;
  onUploadFile: (file: File) => void;
  onError: (error: unknown) => void;
}) {
  const uploadInputRef = useRef<HTMLInputElement>(null);
  const desktopAvailable = isDesktopBridgeAvailable();
  const sealLabel = label.replace(/路径$/, "");
  const title = actionTitle || (desktopAvailable ? `选择并上传${sealLabel}图片` : `上传${sealLabel}图片`);

  async function chooseSealImage() {
    if (actionDisabled) return;

    if (!desktopAvailable) {
      uploadInputRef.current?.click();
      return;
    }

    try {
      const selectedPath = await selectExporterSealImageFile();
      if (!selectedPath) return;

      const dataUrl = await readExporterSealImageFileAsDataUrl(selectedPath);
      if (dataUrl) onUploadFile(dataUrlToFile(dataUrl, selectedPath));
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

function dataUrlToFile(dataUrl: string, selectedPath: string) {
  const match = /^data:(image\/(?:png|jpeg|gif|webp));base64,([a-zA-Z0-9+/=]+)$/.exec(dataUrl);
  if (!match) {
    throw new Error("印章图片内容无效，请重新选择。");
  }

  const binary = window.atob(match[2]);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  const fileName = selectedPath.split(/[\\/]/).pop()?.trim();
  if (!fileName) {
    throw new Error("无法读取印章图片文件名，请重新选择。");
  }

  return new File([bytes], fileName, { type: match[1] });
}
