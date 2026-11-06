/** Some IMEs mark their confirmation key only with the legacy composition code. */
export function isComposingKey(event: Pick<KeyboardEvent, "isComposing" | "keyCode">) {
  return event.isComposing || event.keyCode === 229;
}
