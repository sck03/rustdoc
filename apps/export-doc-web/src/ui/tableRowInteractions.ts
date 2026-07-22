import type { KeyboardEvent } from "react";

/**
 * Returns true only when the table row itself owns focus.
 *
 * Rows can contain buttons, checkboxes or links. Their keyboard events bubble
 * through the row, but must not also activate the row's default action.
 */
export function isDirectTableRowKeyboardEvent<T extends HTMLElement>(event: KeyboardEvent<T>) {
  return event.target === event.currentTarget;
}
