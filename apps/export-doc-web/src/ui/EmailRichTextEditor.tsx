import { Bold, Italic, Link2, List, ListOrdered, RemoveFormatting, Underline } from "lucide-react";
import { useEffect, useRef, useState, type ClipboardEvent, type ReactNode } from "react";

export function EmailRichTextEditor({
  value,
  onChange,
  disabled = false,
  ariaLabel = "邮件正文富文本编辑器",
}: {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  ariaLabel?: string;
}) {
  const editorRef = useRef<HTMLDivElement | null>(null);
  const lastEmittedValueRef = useRef("");
  const savedSelectionRef = useRef<Range | null>(null);
  const [showLinkTools, setShowLinkTools] = useState(false);
  const [linkUrl, setLinkUrl] = useState("");

  useEffect(() => {
    const editor = editorRef.current;
    if (!editor) return;
    const nextValue = normalizeEmailEditorHtml(value);
    if (nextValue === lastEmittedValueRef.current) return;
    if (editor.innerHTML !== nextValue) editor.innerHTML = nextValue;
    lastEmittedValueRef.current = nextValue;
  }, [value]);

  function emitEditorValue() {
    if (disabled) return;
    const editor = editorRef.current;
    if (!editor) return;
    const nextValue = sanitizeEmailHtml(editor.innerHTML);
    if (editor.innerHTML !== nextValue) editor.innerHTML = nextValue;
    lastEmittedValueRef.current = nextValue;
    onChange(nextValue);
  }

  function runCommand(command: string, commandValue?: string) {
    if (disabled) return;
    const editor = editorRef.current;
    if (!editor) return;
    editor.focus();
    if (command === "bold") wrapCurrentSelection(editor, "strong");
    else if (command === "italic") wrapCurrentSelection(editor, "em");
    else if (command === "underline") wrapCurrentSelection(editor, "u");
    else if (command === "insertUnorderedList") replaceSelectionWithList(editor, "ul");
    else if (command === "insertOrderedList") replaceSelectionWithList(editor, "ol");
    else if (command === "removeFormat") replaceSelectionWithPlainText(editor);
    else if (commandValue) insertHtmlAtSelection(editor, commandValue);
    emitEditorValue();
  }

  function handlePaste(event: ClipboardEvent<HTMLDivElement>) {
    if (disabled) return;
    event.preventDefault();
    const richContent = event.clipboardData.getData("text/html");
    const plainContent = event.clipboardData.getData("text/plain");
    const insertHtml = richContent
      ? sanitizeEmailHtml(richContent)
      : escapeHtml(plainContent).replace(/\r?\n/g, "<br>");
    const editor = editorRef.current;
    if (!editor) return;
    editor.focus();
    insertHtmlAtSelection(editor, insertHtml);
    emitEditorValue();
  }

  function applyLink() {
    const href = linkUrl.trim();
    if (!isAllowedEmailHref(href)) return;
    const editor = editorRef.current;
    if (!editor) return;
    editor.focus();
    const selection = window.getSelection();
    if (selection && savedSelectionRef.current) {
      selection.removeAllRanges();
      selection.addRange(savedSelectionRef.current);
    }
    const range = getEditorRange(editor);
    if (range && !range.collapsed) {
      const link = document.createElement("a");
      link.href = href;
      surroundRange(range, link);
      moveCaretAfter(link);
    } else {
      insertHtmlAtSelection(editor, `<a href="${escapeHtml(href)}">${escapeHtml(href)}</a>`);
    }
    emitEditorValue();
    savedSelectionRef.current = null;
    setLinkUrl("");
    setShowLinkTools(false);
  }

  function toggleLinkTools() {
    const editor = editorRef.current;
    const selection = window.getSelection();
    if (editor && selection?.rangeCount && editor.contains(selection.anchorNode)) {
      savedSelectionRef.current = selection.getRangeAt(0).cloneRange();
    }
    setShowLinkTools((current) => !current);
  }

  return (
    <div className={disabled ? "email-rich-text-editor email-rich-text-editor-readonly" : "email-rich-text-editor"}>
      {!disabled ? <div className="email-rich-text-toolbar" role="toolbar" aria-label="邮件正文格式">
        <FormatButton label="加粗" onClick={() => runCommand("bold")}><Bold size={16} aria-hidden="true" /></FormatButton>
        <FormatButton label="斜体" onClick={() => runCommand("italic")}><Italic size={16} aria-hidden="true" /></FormatButton>
        <FormatButton label="下划线" onClick={() => runCommand("underline")}><Underline size={16} aria-hidden="true" /></FormatButton>
        <FormatButton label="项目符号" onClick={() => runCommand("insertUnorderedList")}><List size={16} aria-hidden="true" /></FormatButton>
        <FormatButton label="编号列表" onClick={() => runCommand("insertOrderedList")}><ListOrdered size={16} aria-hidden="true" /></FormatButton>
        <FormatButton label="插入链接" onClick={toggleLinkTools}><Link2 size={16} aria-hidden="true" /></FormatButton>
        <FormatButton label="清除格式" onClick={() => runCommand("removeFormat")}><RemoveFormatting size={16} aria-hidden="true" /></FormatButton>
        {showLinkTools ? <div className="email-rich-text-link-tools">
          <input aria-label="链接地址" value={linkUrl} onChange={(event) => setLinkUrl(event.target.value)} placeholder="https://example.com" />
          <button className="secondary-button" type="button" disabled={!isAllowedEmailHref(linkUrl.trim())} onClick={applyLink}>应用</button>
        </div> : null}
      </div> : null}
      <div
        ref={editorRef}
        className="email-rich-text-surface"
        contentEditable={!disabled}
        suppressContentEditableWarning
        role="textbox"
        aria-label={ariaLabel}
        aria-multiline="true"
        aria-readonly={disabled}
        tabIndex={0}
        onInput={emitEditorValue}
        onBlur={emitEditorValue}
        onPaste={handlePaste}
      />
    </div>
  );
}

export function EmailHtmlPreview({ html, title }: { html: string; title: string }) {
  const safeHtml = sanitizeEmailHtml(html);
  const previewDocument = `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;padding:18px;color:#24302f;background:#fff;font:14px/1.65 system-ui,-apple-system,"Segoe UI",sans-serif;overflow-wrap:anywhere}h1,h2,h3,h4{line-height:1.3}table{width:100%;border-collapse:collapse}td,th{padding:7px 9px;border:1px solid #dce4e2;text-align:left}blockquote{margin:12px 0;padding:8px 12px;border-left:3px solid #8bb8ad;background:#f5faf8}a{color:#176b5b}</style></head><body>${safeHtml}</body></html>`;
  return <iframe className="email-html-preview-frame" title={title} sandbox="" referrerPolicy="no-referrer" srcDoc={previewDocument} />;
}

function FormatButton({ label, onClick, children }: { label: string; onClick: () => void; children: ReactNode }) {
  return <button className="icon-button compact-icon-button" type="button" title={label} aria-label={label}
    onMouseDown={(event) => event.preventDefault()} onClick={onClick}>{children}</button>;
}

function normalizeEmailEditorHtml(value: string) {
  const trimmed = value.trim();
  if (!trimmed) return "";
  if (!/<\/?[a-z][^>]*>/i.test(trimmed)) return escapeHtml(trimmed).replace(/\r?\n/g, "<br>");
  return sanitizeEmailHtml(trimmed);
}

function sanitizeEmailHtml(value: string) {
  const template = document.createElement("template");
  template.innerHTML = value;
  sanitizeChildren(template.content);
  return template.innerHTML.trim();
}

function sanitizeChildren(parent: ParentNode) {
  for (const node of Array.from(parent.childNodes)) {
    if (node.nodeType === Node.COMMENT_NODE) {
      node.parentNode?.removeChild(node);
      continue;
    }
    if (node.nodeType === Node.TEXT_NODE) continue;
    if (!(node instanceof HTMLElement)) {
      node.parentNode?.removeChild(node);
      continue;
    }

    const elementName = node.tagName.toLowerCase();
    if (blockedEmailElements.has(elementName)) {
      node.remove();
      continue;
    }
    if (!allowedEmailElements.has(elementName)) {
      sanitizeChildren(node);
      unwrapElement(node);
      continue;
    }

    sanitizeElementAttributes(node, elementName);
    sanitizeChildren(node);
  }
}

function sanitizeElementAttributes(element: HTMLElement, elementName: string) {
  for (const attribute of Array.from(element.attributes)) {
    const attributeName = attribute.name.toLowerCase();
    const keep = elementName === "a" && ["href", "target", "title", "rel"].includes(attributeName)
      || ["td", "th"].includes(elementName) && ["colspan", "rowspan", "scope"].includes(attributeName);
    if (!keep) element.removeAttribute(attribute.name);
  }

  if (elementName === "a") {
    const href = element.getAttribute("href")?.trim() ?? "";
    if (!isAllowedEmailHref(href)) element.removeAttribute("href");
    const target = element.getAttribute("target")?.trim().toLowerCase() ?? "";
    if (target === "_blank") {
      element.setAttribute("target", "_blank");
      element.setAttribute("rel", "noopener noreferrer");
    } else {
      element.removeAttribute("target");
      element.removeAttribute("rel");
    }
  }

  if (elementName === "td" || elementName === "th") {
    normalizePositiveIntegerAttribute(element, "colspan");
    normalizePositiveIntegerAttribute(element, "rowspan");
    const scope = element.getAttribute("scope")?.trim().toLowerCase() ?? "";
    if (!["row", "col", "rowgroup", "colgroup"].includes(scope)) element.removeAttribute("scope");
  }
}

function normalizePositiveIntegerAttribute(element: HTMLElement, attributeName: string) {
  const parsed = Number(element.getAttribute(attributeName));
  if (!Number.isInteger(parsed) || parsed < 1 || parsed > 100) element.removeAttribute(attributeName);
  else element.setAttribute(attributeName, String(parsed));
}

function unwrapElement(element: HTMLElement) {
  const parent = element.parentNode;
  if (!parent) return;
  while (element.firstChild) parent.insertBefore(element.firstChild, element);
  parent.removeChild(element);
}

function isAllowedEmailHref(href: string) {
  if (!href || /[\u0000-\u001f\u007f]/.test(href)) return false;
  return href.startsWith("#") || /^(https?:\/\/|mailto:|tel:)/i.test(href);
}

function escapeHtml(value: string) {
  return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}

function getEditorRange(editor: HTMLElement) {
  const selection = window.getSelection();
  if (selection?.rangeCount) {
    const range = selection.getRangeAt(0);
    if (editor.contains(range.commonAncestorContainer)) {
      return range;
    }
  }

  const range = document.createRange();
  range.selectNodeContents(editor);
  range.collapse(false);
  selection?.removeAllRanges();
  selection?.addRange(range);
  return range;
}

function wrapCurrentSelection(editor: HTMLElement, tagName: "strong" | "em" | "u") {
  const range = getEditorRange(editor);
  if (!range || range.collapsed) return;
  const wrapper = document.createElement(tagName);
  surroundRange(range, wrapper);
  moveCaretAfter(wrapper);
}

function surroundRange(range: Range, wrapper: HTMLElement) {
  const contents = range.extractContents();
  wrapper.appendChild(contents);
  range.insertNode(wrapper);
}

function replaceSelectionWithList(editor: HTMLElement, tagName: "ul" | "ol") {
  const range = getEditorRange(editor);
  if (!range) return;
  const list = document.createElement(tagName);
  const lines = range.toString().split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  for (const line of lines.length > 0 ? lines : [""]) {
    const item = document.createElement("li");
    if (line) item.textContent = line;
    else item.appendChild(document.createElement("br"));
    list.appendChild(item);
  }
  range.deleteContents();
  range.insertNode(list);
  const firstItem = list.firstElementChild;
  if (firstItem) moveCaretInside(firstItem);
}

function replaceSelectionWithPlainText(editor: HTMLElement) {
  const range = getEditorRange(editor);
  if (!range || range.collapsed) return;
  const text = document.createTextNode(range.toString());
  range.deleteContents();
  range.insertNode(text);
  moveCaretAfter(text);
}

function insertHtmlAtSelection(editor: HTMLElement, html: string) {
  const range = getEditorRange(editor);
  if (!range) return;
  const fragment = range.createContextualFragment(html);
  const lastNode = fragment.lastChild;
  range.deleteContents();
  range.insertNode(fragment);
  if (lastNode) moveCaretAfter(lastNode);
}

function moveCaretAfter(node: Node) {
  const range = document.createRange();
  range.setStartAfter(node);
  range.collapse(true);
  setSelectionRange(range);
}

function moveCaretInside(node: Node) {
  const range = document.createRange();
  range.selectNodeContents(node);
  range.collapse(false);
  setSelectionRange(range);
}

function setSelectionRange(range: Range) {
  const selection = window.getSelection();
  selection?.removeAllRanges();
  selection?.addRange(range);
}

const allowedEmailElements = new Set([
  "a", "b", "blockquote", "br", "div", "em", "h1", "h2", "h3", "h4", "hr", "i", "li", "ol", "p",
  "s", "span", "strong", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "u", "ul",
]);

const blockedEmailElements = new Set([
  "audio", "base", "button", "canvas", "embed", "form", "frame", "frameset", "iframe", "input", "link", "math",
  "meta", "object", "option", "script", "select", "source", "style", "svg", "textarea", "video",
]);
