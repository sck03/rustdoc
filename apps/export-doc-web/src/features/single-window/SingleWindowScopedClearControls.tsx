import { useEffect, useMemo, useState } from "react";
import { Eraser, RotateCcw } from "lucide-react";
import type { SingleWindowScopedClearOption } from "./singleWindowEditorTools.ts";

export type SingleWindowScopedClearGroup = {
  key: string;
  label: string;
};

export function SingleWindowScopedClearControls({
  groups,
  optionsByGroup,
  disabled,
  onClearGroup,
  onClearCategory,
}: {
  groups: readonly SingleWindowScopedClearGroup[];
  optionsByGroup: Record<string, readonly SingleWindowScopedClearOption[]>;
  disabled: boolean;
  onClearGroup: (groupKey: string) => void;
  onClearCategory: (groupKey: string, categoryKey: string, categoryLabel: string) => void;
}) {
  const [selectedGroupKey, setSelectedGroupKey] = useState(groups[0]?.key ?? "");
  const selectedGroup = groups.find((group) => group.key === selectedGroupKey) ?? groups[0];
  const groupKey = selectedGroup?.key ?? "";
  const categoryOptions = useMemo(() => optionsByGroup[groupKey] ?? [], [groupKey, optionsByGroup]);
  const [selectedCategoryKey, setSelectedCategoryKey] = useState(categoryOptions[0]?.key ?? "");
  const selectedCategory =
    categoryOptions.find((option) => option.key === selectedCategoryKey) ?? categoryOptions[0] ?? null;

  useEffect(() => {
    if (!groups.some((group) => group.key === selectedGroupKey)) {
      setSelectedGroupKey(groups[0]?.key ?? "");
    }
  }, [groups, selectedGroupKey]);

  useEffect(() => {
    if (!categoryOptions.some((option) => option.key === selectedCategoryKey)) {
      setSelectedCategoryKey(categoryOptions[0]?.key ?? "");
    }
  }, [categoryOptions, selectedCategoryKey]);

  if (!groupKey) {
    return null;
  }

  return (
    <div className="single-window-section-tools">
      <span className="single-window-tool-heading">恢复</span>
      {groups.length > 1 ? (
        <select
          className="single-window-scope-select"
          aria-label="恢复范围"
          value={groupKey}
          disabled={disabled}
          onChange={(event) => setSelectedGroupKey(event.target.value)}
        >
          {groups.map((group) => (
            <option key={group.key} value={group.key}>
              {group.label}
            </option>
          ))}
        </select>
      ) : null}
      <button
        className="command-button secondary scoped-clear-button"
        type="button"
        title={`${selectedGroup.label} - 恢复整个分组到当前发票建议值`}
        disabled={disabled}
        onClick={() => onClearGroup(groupKey)}
      >
        <RotateCcw size={15} aria-hidden="true" />
        <span>分组</span>
      </button>
      {categoryOptions.length > 0 ? (
        <select
          className="single-window-scope-select"
          aria-label={`${selectedGroup.label} 恢复类别`}
          value={selectedCategory?.key ?? ""}
          disabled={disabled}
          onChange={(event) => setSelectedCategoryKey(event.target.value)}
        >
          {categoryOptions.map((option) => (
            <option key={option.key} value={option.key} title={option.description}>
              {option.label}
            </option>
          ))}
        </select>
      ) : null}
      <button
        className="command-button secondary scoped-clear-button"
        type="button"
        title={`${selectedGroup.label} - 恢复所选类别到当前发票建议值`}
        disabled={disabled || !selectedCategory}
        onClick={() => {
          if (selectedCategory) {
            onClearCategory(groupKey, selectedCategory.key, selectedCategory.label);
          }
        }}
      >
        <Eraser size={15} aria-hidden="true" />
        <span>类别</span>
      </button>
    </div>
  );
}
