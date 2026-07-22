import { useState, type ReactNode } from "react";
import {
  ArrowLeft,
  Eraser,
  FileCheck2,
  LockKeyholeOpen,
  RefreshCw,
  RotateCcw,
  Save,
  Undo2,
  WandSparkles,
} from "lucide-react";

export function SingleWindowDocumentActionBar({
  title,
  titleIcon,
  formId,
  isBusy,
  isDocumentReady,
  isInvoiceIdValid,
  canUndo,
  canOperate,
  scopedClearControls,
  onBack,
  onRefresh,
  onRestoreDefaults,
  onFillEmptyFields,
  onClearManualOverrides,
  onOpenLockedFields,
  onUndo,
  onBuildReview,
}: {
  title: string;
  titleIcon: ReactNode;
  formId: string;
  isBusy: boolean;
  isDocumentReady: boolean;
  isInvoiceIdValid: boolean;
  canUndo: boolean;
  canOperate: boolean;
  scopedClearControls: ReactNode;
  onBack: () => void;
  onRefresh: () => void;
  onRestoreDefaults: () => void;
  onFillEmptyFields: () => void;
  onClearManualOverrides: () => void;
  onOpenLockedFields: () => void;
  onUndo: () => void;
  onBuildReview: () => void;
}) {
  const [viewMode, setViewMode] = useState<"standard" | "advanced">("standard");
  const isActionDisabled = isBusy || !isDocumentReady || !isInvoiceIdValid;

  return (
    <div className="editor-toolbar single-window-document-toolbar">
      <div className="single-window-toolbar-head">
        <button className="command-button secondary" type="button" onClick={onBack}>
          <ArrowLeft size={17} aria-hidden="true" />
          <span>返回发票</span>
        </button>
        <div className="editor-title single-window-document-title">
          {titleIcon}
          <span>{title}</span>
        </div>
      </div>

      <div className="single-window-command-band" aria-label="草稿操作">
        <div className="single-window-view-mode" role="group" aria-label="操作模式">
          <button
            className={viewMode === "standard" ? "active" : ""}
            type="button"
            aria-pressed={viewMode === "standard"}
            onClick={() => setViewMode("standard")}
          >
            标准模式
          </button>
          <button
            className={viewMode === "advanced" ? "active" : ""}
            type="button"
            aria-pressed={viewMode === "advanced"}
            onClick={() => setViewMode("advanced")}
          >
            高级模式
          </button>
        </div>
        <div className="single-window-tool-group" aria-label="草稿处理">
          <span className="single-window-tool-heading">草稿</span>
          <button
            className="icon-button"
            type="button"
            title="刷新草稿" aria-label="刷新草稿"
            disabled={isBusy || !isInvoiceIdValid}
            onClick={onRefresh}
          >
            <RefreshCw size={17} aria-hidden="true" />
          </button>
          {viewMode === "advanced" ? (
            <button
              className="command-button secondary"
              type="button"
              title="按当前发票重新取默认值"
              disabled={!canOperate || isActionDisabled}
              onClick={onRestoreDefaults}
            >
              <RotateCcw size={16} aria-hidden="true" />
              <span>取默认</span>
            </button>
          ) : null}
          <button
            className="command-button secondary"
            type="button"
            title="仅回填当前空白字段"
            disabled={!canOperate || isActionDisabled}
            onClick={onFillEmptyFields}
          >
            <WandSparkles size={16} aria-hidden="true" />
            <span>回填空白</span>
          </button>
          {viewMode === "advanced" ? (
            <button
              className="command-button secondary"
              type="button"
              title="清空人工覆盖并恢复到建议值"
              disabled={!canOperate || isActionDisabled}
              onClick={onClearManualOverrides}
            >
              <Eraser size={16} aria-hidden="true" />
              <span>清覆盖</span>
            </button>
          ) : null}
        </div>

        {viewMode === "advanced" ? scopedClearControls : null}

        <div className="single-window-tool-group" aria-label="校验与辅助">
          <span className="single-window-tool-heading">校验</span>
          {viewMode === "advanced" ? (
            <>
              <button
                className="command-button secondary"
                type="button"
                disabled={!canOperate || isActionDisabled}
                onClick={onOpenLockedFields}
              >
                <LockKeyholeOpen size={16} aria-hidden="true" />
                <span>字段锁定</span>
              </button>
              <button className="icon-button" type="button" title="撤销上一次工具动作" aria-label="撤销上一次工具动作" disabled={!canOperate || isBusy || !canUndo} onClick={onUndo}>
                <Undo2 size={17} aria-hidden="true" />
              </button>
            </>
          ) : null}
          <button
            className="command-button secondary"
            type="button"
            disabled={isBusy || !isInvoiceIdValid}
            onClick={onBuildReview}
          >
            <FileCheck2 size={16} aria-hidden="true" />
            <span>预检</span>
          </button>
        </div>

        <div className="single-window-tool-group single-window-tool-group-primary" aria-label="保存">
          <button className="command-button" type="submit" form={formId} disabled={!canOperate || isBusy || !isDocumentReady}>
            <Save size={17} aria-hidden="true" />
            <span>保存草稿</span>
          </button>
        </div>
      </div>
    </div>
  );
}
