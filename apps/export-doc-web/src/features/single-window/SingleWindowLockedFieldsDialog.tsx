import { LockKeyholeOpen, X } from "lucide-react";
import { ApiSingleWindowLockedFieldDto } from "../../api/index.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useModalDialog } from "../../ui/useModalDialog.ts";

export function SingleWindowLockedFieldsDialog({
  title,
  fields,
  selectedKeys,
  isBusy,
  onClose,
  onToggleField,
  onToggleAll,
  onUnlockSelected,
}: {
  title: string;
  fields: ApiSingleWindowLockedFieldDto[];
  selectedKeys: Set<string>;
  isBusy: boolean;
  onClose: () => void;
  onToggleField: (key: string) => void;
  onToggleAll: () => void;
  onUnlockSelected: () => void;
}) {
  const selectedCount = selectedKeys.size;
  const allSelected = fields.length > 0 && selectedCount === fields.length;
  const dialogRef = useModalDialog<HTMLDivElement>(onClose, { canClose: !isBusy });

  return (
    <div className="workspace-modal-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget && !isBusy) {
        onClose();
      }
    }}>
      <div ref={dialogRef} className="workspace-modal-dialog" role="dialog" aria-modal="true" aria-labelledby="single-window-lock-title">
        <header className="workspace-modal-header">
          <div className="workspace-modal-title">
            <LockKeyholeOpen size={18} aria-hidden="true" />
            <h2 id="single-window-lock-title">{title}</h2>
            <span>{fields.length}</span>
          </div>
          <button className="icon-button" type="button" title="关闭" aria-label="关闭" onClick={onClose} disabled={isBusy}>
            <X size={18} aria-hidden="true" />
          </button>
        </header>

        <div className="workspace-modal-toolbar">
          <label className="single-window-lock-select-all">
            <input type="checkbox" checked={allSelected} disabled={fields.length === 0 || isBusy} onChange={onToggleAll} />
            <span>全选</span>
          </label>
          <span>已选 {selectedCount}</span>
        </div>

        {fields.length === 0 ? (
          <InlineNotice tone="info">当前没有人工锁定字段。</InlineNotice>
        ) : (
          <ResponsiveTableFrame className="single-window-lock-table-frame" label="锁定字段列表">
            <table className="single-window-lock-table">
              <thead>
                <tr>
                  <th>选择</th>
                  <th>字段</th>
                  <th>当前值</th>
                  <th>建议值</th>
                </tr>
              </thead>
              <tbody>
                {fields.map((field) => (
                  <tr key={field.key}>
                    <td>
                      <input
                        aria-label={`选择 ${field.displayName}`}
                        type="checkbox"
                        checked={selectedKeys.has(field.key)}
                        disabled={isBusy}
                        onChange={() => onToggleField(field.key)}
                      />
                    </td>
                    <td className="strong-cell">{field.displayName}</td>
                    <td className="message-cell" title={readLockedValue(field.currentValue)}>
                      {readLockedValue(field.currentValue)}
                    </td>
                    <td className="message-cell" title={readLockedValue(field.suggestedValue)}>
                      {readLockedValue(field.suggestedValue)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </ResponsiveTableFrame>
        )}

        <footer className="workspace-modal-footer">
          <button className="command-button secondary" type="button" onClick={onClose} disabled={isBusy}>
            <span>关闭</span>
          </button>
          <button className="command-button" type="button" onClick={onUnlockSelected} disabled={isBusy || selectedCount === 0}>
            <LockKeyholeOpen size={17} aria-hidden="true" />
            <span>解锁选中</span>
          </button>
        </footer>
      </div>
    </div>
  );
}

function readLockedValue(value?: string) {
  return value?.trim() ? value : "空白";
}
