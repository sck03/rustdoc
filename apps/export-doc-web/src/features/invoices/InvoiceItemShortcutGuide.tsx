import { Keyboard } from "lucide-react";

const primaryShortcuts = [
  { keys: "Enter / Tab", action: "下一行" },
  { keys: "Shift + Enter / Tab", action: "上一行" },
  { keys: "↑ ↓", action: "上下换行" },
  { keys: "Ctrl + ↑ ↓", action: "选择联想" },
  { keys: "Ctrl + D", action: "向下填充" },
  { keys: "Ctrl + Z / Y", action: "撤销 / 重做" },
  { keys: "Insert", action: "新增行" },
];

const secondaryShortcuts = [
  { keys: "Enter / Tab", action: "采用当前联想" },
  { keys: "Esc", action: "关闭联想" },
  { keys: "Ctrl + Shift + D", action: "复制当前行" },
  { keys: "Alt + ↑ ↓", action: "移动当前行" },
  { keys: "Shift + 方向键", action: "连续选择单元格" },
  { keys: "Ctrl + C / Delete", action: "复制 / 清空选区" },
];

export function InvoiceItemShortcutGuide() {
  return (
    <div className="item-shortcut-guide" role="note" aria-label="商品明细键盘快捷键说明">
      <div className="item-shortcut-guide-title">
        <Keyboard size={16} aria-hidden="true" />
        <strong>键盘操作</strong>
      </div>
      <div className="item-shortcut-list">
        {primaryShortcuts.map((shortcut) => (
          <span className="item-shortcut" key={`${shortcut.keys}-${shortcut.action}`}>
            <kbd>{shortcut.keys}</kbd>
            <span>{shortcut.action}</span>
          </span>
        ))}
      </div>
      <details className="item-shortcut-more">
        <summary>更多</summary>
        <div className="item-shortcut-list item-shortcut-list-secondary">
          {secondaryShortcuts.map((shortcut) => (
            <span className="item-shortcut" key={`${shortcut.keys}-${shortcut.action}`}>
              <kbd>{shortcut.keys}</kbd>
              <span>{shortcut.action}</span>
            </span>
          ))}
        </div>
      </details>
    </div>
  );
}
