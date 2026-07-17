import { memo, type KeyboardEvent, type MouseEvent, useEffect, useId, useRef, useState } from "react";
import type { ApiInvoiceItemDto } from "../../api/index.ts";
import type { EditableInvoiceItemField, InvoiceItemColumnDefinition } from "./invoiceItemTableModel.ts";
import { invoiceItemNumberInputValue, readItemNumberValue, readItemTextValue, readInvoiceItemNumberInput } from "./invoiceItemsEditorModel.ts";
import { normalizeText } from "../../ui/formUtils.ts";
import { filterInvoiceItemHistoryOptionsByPrefix } from "./invoiceItemHistory.ts";

export type InvoiceItemCellInputProps = {
  item: ApiInvoiceItemDto;
  index: number;
  column: InvoiceItemColumnDefinition;
  selected: boolean;
  options?: string[];
  disabled?: boolean;
  onFocus: () => void;
  onMouseDown: (event: MouseEvent<HTMLInputElement>) => void;
  onChange: (value: string | number | undefined) => void;
};

export const InvoiceItemCellInput = memo(function InvoiceItemCellInput({
  item,
  index,
  column,
  selected,
  options,
  disabled,
  onFocus,
  onMouseDown,
  onChange,
}: InvoiceItemCellInputProps) {
  const ariaLabel = `第 ${index + 1} 行${column.ariaName}`;
  const historyPanelId = `invoice-item-history-${useId().replace(/:/g, "-")}`;
  const inputRef = useRef<HTMLInputElement>(null);
  const pendingAutocompleteSelectionRef = useRef<{ start: number; end: number } | null>(null);
  const [isFocused, setIsFocused] = useState(false);
  const [isHistoryOpen, setIsHistoryOpen] = useState(false);
  const [activeOptionIndex, setActiveOptionIndex] = useState(0);
  const currentInputText =
    column.kind === "number"
      ? invoiceItemNumberInputValue(readItemNumberValue(item, column.field))
      : readItemTextValue(item, column.field);
  const inlineOptions = filterInvoiceItemHistoryOptionsByPrefix(options ?? [], currentInputText).slice(0, 6);
  const canShowInlineOptions = isFocused && isHistoryOpen && !disabled && inlineOptions.length > 0;
  const activeOptionId = canShowInlineOptions ? `${historyPanelId}-option-${activeOptionIndex}` : undefined;

  useEffect(() => {
    if (!isFocused || inlineOptions.length === 0) {
      setActiveOptionIndex(0);
      return;
    }

    setActiveOptionIndex((current) => Math.max(0, Math.min(current, inlineOptions.length - 1)));
  }, [inlineOptions.length, isFocused]);

  useEffect(() => {
    const selection = pendingAutocompleteSelectionRef.current;
    const input = inputRef.current;
    if (!selection || !input || input.type === "number" || document.activeElement !== input) {
      pendingAutocompleteSelectionRef.current = null;
      return;
    }

    try {
      input.setSelectionRange(selection.start, selection.end);
    } catch {
      // Some browser/input combinations do not support selection ranges.
    } finally {
      pendingAutocompleteSelectionRef.current = null;
    }
  }, [currentInputText]);

  function handleFocus() {
    setIsFocused(true);
    setIsHistoryOpen(true);
    onFocus();
  }

  function applyInlineOption(option: string) {
    onChange(column.kind === "number" ? readInvoiceItemNumberInput(option) : option);
    setIsHistoryOpen(false);
  }

  function applyActiveInlineOption() {
    const option = inlineOptions[activeOptionIndex] ?? inlineOptions[0];
    if (option) {
      applyInlineOption(option);
    }
  }

  function handleInputChange(rawValue: string) {
    const previousPrefix = normalizeText(currentInputText);
    const nextPrefix = normalizeText(rawValue);
    const isForwardTyping = nextPrefix.length > previousPrefix.length;
    const matchingOptions = filterInvoiceItemHistoryOptionsByPrefix(options ?? [], rawValue);

    if (!disabled && isForwardTyping && matchingOptions.length === 1) {
      const option = matchingOptions[0];
      if (column.kind !== "number") {
        pendingAutocompleteSelectionRef.current = {
          start: Math.min(rawValue.length, option.length),
          end: option.length,
        };
      }

      onChange(column.kind === "number" ? readInvoiceItemNumberInput(option) : option);
      setIsHistoryOpen(false);
      return;
    }

    setIsHistoryOpen(matchingOptions.length > 0);
    onChange(column.kind === "number" ? readInvoiceItemNumberInput(rawValue) : rawValue);
  }

  function handleOptionKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    const nativeEvent = event.nativeEvent as globalThis.KeyboardEvent;
    if (nativeEvent.isComposing || event.altKey || event.metaKey || inlineOptions.length === 0) {
      return;
    }

    if (event.ctrlKey && (event.key === "ArrowDown" || event.key === "ArrowUp")) {
      event.preventDefault();
      event.stopPropagation();
      setIsHistoryOpen(true);
      setActiveOptionIndex((current) => {
        if (!isHistoryOpen) {
          return 0;
        }

        const nextIndex = event.key === "ArrowDown" ? current + 1 : current - 1;
        return (nextIndex + inlineOptions.length) % inlineOptions.length;
      });
      return;
    }

    if (event.ctrlKey && event.key === "Home" && isHistoryOpen) {
      event.preventDefault();
      event.stopPropagation();
      setActiveOptionIndex(0);
      return;
    }

    if (event.ctrlKey && event.key === "End" && isHistoryOpen) {
      event.preventDefault();
      event.stopPropagation();
      setActiveOptionIndex(inlineOptions.length - 1);
      return;
    }

    if (event.ctrlKey) {
      return;
    }

    if (event.key === "Escape" && isHistoryOpen) {
      event.preventDefault();
      event.stopPropagation();
      setIsHistoryOpen(false);
      return;
    }

    if (event.key === "Enter" && isHistoryOpen) {
      event.preventDefault();
      event.stopPropagation();
      applyActiveInlineOption();
      return;
    }

    if (event.key === "Tab" && isHistoryOpen) {
      applyActiveInlineOption();
    }
  }

  const inlineOptionPanel = canShowInlineOptions ? (
    <div className="item-cell-history-panel" id={historyPanelId} role="listbox" aria-label={`${ariaLabel}历史值`}>
      {inlineOptions.map((option, optionIndex) => (
        <button
          className={`item-cell-history-option${optionIndex === activeOptionIndex ? " item-cell-history-option-active" : ""}`}
          id={`${historyPanelId}-option-${optionIndex}`}
          key={option}
          type="button"
          role="option"
          aria-selected={optionIndex === activeOptionIndex}
          title={option}
          onMouseDown={(event) => event.preventDefault()}
          onMouseEnter={() => setActiveOptionIndex(optionIndex)}
          onClick={() => applyInlineOption(option)}
        >
          {option}
        </button>
      ))}
    </div>
  ) : null;

  if (column.kind === "number") {
    return (
      <div className="item-cell-editor" onBlur={() => {
        setIsFocused(false);
        setIsHistoryOpen(false);
      }}>
        <input
          ref={inputRef}
          className={`item-cell-input item-number-input${selected ? " item-cell-selected" : ""}`}
          type="number"
          step="0.01"
          aria-label={ariaLabel}
          aria-controls={canShowInlineOptions ? historyPanelId : undefined}
          aria-expanded={canShowInlineOptions}
          aria-activedescendant={activeOptionId}
          data-invoice-item-row={index}
          data-invoice-item-field={column.field}
          value={invoiceItemNumberInputValue(readItemNumberValue(item, column.field))}
          disabled={disabled}
          onFocus={handleFocus}
          onMouseDown={onMouseDown}
          onKeyDown={handleOptionKeyDown}
          onChange={(event) => {
            handleInputChange(event.target.value);
          }}
        />
        {inlineOptionPanel}
      </div>
    );
  }

  return (
    <div className="item-cell-editor" onBlur={() => {
      setIsFocused(false);
      setIsHistoryOpen(false);
    }}>
      <input
        ref={inputRef}
        className={`item-cell-input${selected ? " item-cell-selected" : ""}`}
        aria-label={ariaLabel}
        aria-controls={canShowInlineOptions ? historyPanelId : undefined}
        aria-expanded={canShowInlineOptions}
        aria-activedescendant={activeOptionId}
        data-invoice-item-row={index}
        data-invoice-item-field={column.field}
        value={readItemTextValue(item, column.field)}
        disabled={disabled}
        onFocus={handleFocus}
        onMouseDown={onMouseDown}
        onKeyDown={handleOptionKeyDown}
        onChange={(event) => {
          handleInputChange(event.target.value);
        }}
      />
      {inlineOptionPanel}
    </div>
  );
}, areInvoiceItemCellInputPropsEqual);

function areInvoiceItemCellInputPropsEqual(previous: InvoiceItemCellInputProps, next: InvoiceItemCellInputProps) {
  return (
    previous.item === next.item &&
    previous.index === next.index &&
    previous.column === next.column &&
    previous.selected === next.selected &&
    previous.disabled === next.disabled &&
    areStringArraysEqual(previous.options, next.options)
  );
}

function areStringArraysEqual(previous?: string[], next?: string[]) {
  if (previous === next) {
    return true;
  }

  if (!previous || !next || previous.length !== next.length) {
    return false;
  }

  return previous.every((value, index) => value === next[index]);
}

