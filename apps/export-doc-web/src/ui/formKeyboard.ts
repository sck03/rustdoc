import { type KeyboardEvent as ReactKeyboardEvent } from "react";

export function handleEnterAsTabFormKeyDown(event: ReactKeyboardEvent<HTMLFormElement>) {
  if (
    event.defaultPrevented ||
    !["Enter", "ArrowDown", "ArrowUp"].includes(event.key) ||
    event.ctrlKey ||
    event.metaKey ||
    event.altKey
  ) {
    return;
  }

  const target = event.target;
  if (!(target instanceof HTMLElement) || !isExcelLikeFormNavigationTarget(target)) {
    return;
  }

  if (event.key !== "Enter" && !isArrowFormNavigationTarget(target)) {
    return;
  }

  event.preventDefault();
  if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    moveFocusVerticallyWithinFieldGrid(event.currentTarget, target, event.key === "ArrowUp");
    return;
  }

  moveFocusWithinForm(event.currentTarget, target, event.shiftKey);
}

function isExcelLikeFormNavigationTarget(target: HTMLElement) {
  if (
    target.closest(
      "button, a, [data-form-keyboard='native'], .item-editor-frame, .reference-catalog-table, .batch-export-table, .container-packing-cargo-table",
    )
  ) {
    return false;
  }

  if (target instanceof HTMLTextAreaElement) {
    return false;
  }

  if (target instanceof HTMLInputElement) {
    return !["button", "checkbox", "file", "hidden", "radio", "reset", "submit"].includes(target.type);
  }

  return target instanceof HTMLSelectElement;
}

function isArrowFormNavigationTarget(target: HTMLElement) {
  return target instanceof HTMLInputElement || target instanceof HTMLSelectElement;
}

function moveFocusWithinForm(form: HTMLFormElement, current: HTMLElement, backwards: boolean) {
  const focusable = Array.from(
    form.querySelectorAll<HTMLElement>(
      "input:not([type='hidden']), select, textarea, [tabindex]:not([tabindex='-1'])",
    ),
  ).filter((element) => isFocusableFormField(element));
  const currentIndex = focusable.indexOf(current);
  if (currentIndex < 0 || focusable.length === 0) {
    return;
  }

  const nextIndex = backwards
    ? (currentIndex - 1 + focusable.length) % focusable.length
    : (currentIndex + 1) % focusable.length;
  const next = focusable[nextIndex];
  next.focus();
  next.scrollIntoView({ block: "nearest", inline: "nearest" });
  selectTextIfSupported(next);
}

function moveFocusVerticallyWithinFieldGrid(form: HTMLFormElement, current: HTMLElement, backwards: boolean) {
  const grid = current.closest<HTMLElement>(".field-grid");
  if (!grid || !form.contains(grid)) {
    moveFocusWithinForm(form, current, backwards);
    return;
  }

  const focusable = Array.from(
    grid.querySelectorAll<HTMLElement>(
      "input:not([type='hidden']), select, textarea, [tabindex]:not([tabindex='-1'])",
    ),
  ).filter((element) => isFocusableFormField(element));
  const currentIndex = focusable.indexOf(current);
  const columnCount = readGridColumnCount(grid);
  if (currentIndex < 0 || focusable.length === 0 || columnCount <= 0) {
    moveFocusWithinForm(form, current, backwards);
    return;
  }

  const nextIndex = currentIndex + (backwards ? -columnCount : columnCount);
  if (nextIndex < 0 || nextIndex >= focusable.length) {
    moveFocusWithinForm(form, current, backwards);
    return;
  }

  const next = focusable[nextIndex];
  next.focus();
  next.scrollIntoView({ block: "nearest", inline: "nearest" });
  selectTextIfSupported(next);
}

function readGridColumnCount(grid: HTMLElement) {
  const templateColumns = window.getComputedStyle(grid).gridTemplateColumns;
  if (!templateColumns || templateColumns === "none") {
    return 1;
  }

  return Math.max(1, templateColumns.split(" ").filter(Boolean).length);
}

function isFocusableFormField(element: HTMLElement) {
  if (
    element.hasAttribute("disabled") ||
    element.getAttribute("aria-hidden") === "true" ||
    element.closest(
      "button, a, [data-form-keyboard='native'], .item-editor-frame, .reference-catalog-table, .batch-export-table, .container-packing-cargo-table",
    )
  ) {
    return false;
  }

  if (
    element instanceof HTMLInputElement ||
    element instanceof HTMLSelectElement ||
    element instanceof HTMLTextAreaElement
  ) {
    if (
      element instanceof HTMLInputElement &&
      ["button", "checkbox", "file", "hidden", "radio", "reset", "submit"].includes(element.type)
    ) {
      return false;
    }

    return !element.disabled && element.getClientRects().length > 0;
  }

  return element.tabIndex >= 0 && element.getClientRects().length > 0;
}

function selectTextIfSupported(element: HTMLElement) {
  if (element instanceof HTMLTextAreaElement) {
    element.select();
    return;
  }

  if (!(element instanceof HTMLInputElement)) {
    return;
  }

  if (
    [
      "email",
      "password",
      "search",
      "tel",
      "text",
      "url",
    ].includes(element.type)
  ) {
    element.select();
  }
}
