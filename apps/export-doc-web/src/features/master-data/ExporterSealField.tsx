import { FolderOpen, Trash2, Upload } from "lucide-react";
import { useMemo, useRef } from "react";
import {
  isDesktopBridgeAvailable,
  readExporterSealImageFile,
  selectExporterSealImageFile,
} from "../../desktop/desktopBridge.ts";
import { PathField } from "../../ui/PathField.tsx";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";

export type ExporterSealType = "document" | "customs";

export function ExporterSealField({
  label,
  scopeKey,
  value,
  inputReadOnly,
  actionDisabled,
  actionTitle,
  onPathChange,
  onUploadFile,
  onError,
}: {
  label: string;
  scopeKey: string;
  value: string;
  inputReadOnly?: boolean;
  actionDisabled?: boolean;
  actionTitle?: string;
  onPathChange?: (value: string) => void;
  onUploadFile: (file: File) => void;
  onError: (error: unknown) => void;
}) {
  const uploadInputRef = useRef<HTMLInputElement>(null);
  const scope = useMemo(() => ({ scopeKey, value, actionDisabled }), [scopeKey, value, actionDisabled]);
  const browserSelection = useRef(scope);
  const readImage = useAbortableOperation(scope, { cancelPrevious: true });
  const desktopAvailable = isDesktopBridgeAvailable();
  const sealLabel = label.replace(/路径$/, "");
  const title = actionTitle || (desktopAvailable ? `选择并上传${sealLabel}图片` : `上传${sealLabel}图片`);

  async function chooseSealImage() {
    if (actionDisabled) return;

    if (!desktopAvailable) {
      browserSelection.current = scope;
      uploadInputRef.current?.click();
      return;
    }

    try {
      const file = await readImage(async signal => {
        const selectedPath = await selectExporterSealImageFile();
        signal.throwIfAborted();
        if (!selectedPath) return;
        const bytes = await readExporterSealImageFile(selectedPath);
        signal.throwIfAborted();
        return bytes ? sealBytesToFile(bytes, selectedPath) : undefined;
      });
      if (file) onUploadFile(file);
    } catch (error) {
      if (!isAbortError(error)) onError(error);
    }
  }

  return (
    <>
      <PathField
        readOnly={inputReadOnly}
        label={label}
        value={value}
        onChange={onPathChange ?? (() => undefined)}
        actions={
          <>
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
            {onPathChange ? (
              <button
                className="icon-button compact-icon-button"
                type="button"
                title={`清除${sealLabel}`}
                aria-label={`清除${sealLabel}`}
                disabled={actionDisabled || !value}
                onClick={() => onPathChange("")}
              >
                <Trash2 size={15} aria-hidden="true" />
              </button>
            ) : null}
          </>
        }
      />
      <input
        ref={uploadInputRef}
        hidden
        type="file"
        aria-label={`${sealLabel}图片文件`}
        accept="image/png,image/jpeg,.png,.jpg,.jpeg"
        disabled={actionDisabled}
        onChange={(event) => {
          const file = event.currentTarget.files?.[0];
          event.currentTarget.value = "";
          if (file && !actionDisabled && browserSelection.current === scope) onUploadFile(file);
        }}
      />
    </>
  );
}

function sealBytesToFile(bytes: ArrayBuffer, selectedPath: string) {
  const fileName = selectedPath.split(/[\\/]/).pop()?.trim();
  if (!fileName) {
    throw new Error("无法读取印章图片文件名，请重新选择。");
  }

  return new File([bytes], fileName, { type: /\.png$/i.test(fileName) ? "image/png" : "image/jpeg" });
}
