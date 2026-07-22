import fs from "node:fs";
import path from "node:path";
import ts from "../apps/export-doc-web/node_modules/typescript/lib/typescript.js";

const root = path.resolve(import.meta.dirname, "../apps/export-doc-web/src");
const failures = [];
const unifiedFeedbackFiles = new Set([
  "features/invoices/InvoiceEditorPage.tsx",
  "features/invoices/InvoiceListPage.tsx",
  "features/payments/PaymentEditorPage.tsx",
  "features/payments/PaymentListPage.tsx",
  "features/master-data/MasterDataEditorPage.tsx",
  "features/master-data/MasterDataListPage.tsx",
  "features/settings/SettingsPage.tsx",
  "features/settings/UserManagementPanel.tsx",
  "features/settings/PermissionTemplateManagementPanel.tsx",
]);

for (const file of walk(root)) {
  if (!file.endsWith(".tsx") && !file.endsWith(".ts")) continue;
  const sourceText = fs.readFileSync(file, "utf8");
  const sourceRelativePath = path.relative(root, file).replaceAll("\\", "/");
  const source = ts.createSourceFile(file, sourceText, ts.ScriptTarget.Latest, true, file.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  if (/\bwindow\.confirm\s*\(|(^|[^.\w])confirm\s*\(/m.test(sourceText)) {
    failures.push(`${path.relative(path.resolve(import.meta.dirname, ".."), file)}: 不允许使用原生 confirm，请使用应用确认组件`);
  }
  if (unifiedFeedbackFiles.has(sourceRelativePath) && /className=["'](?:alert|success-alert|error-alert)["']/.test(sourceText)) {
    failures.push(`${sourceRelativePath}: 核心工作流必须使用 InlineNotice 提供统一反馈语义`);
  }
  visit(source, source);
}

if (failures.length) {
  process.stderr.write(`${failures.join("\n")}\n`);
  process.exit(1);
}

process.stdout.write("frontend accessibility contracts passed\n");

function visit(node, source) {
  if (ts.isJsxElement(node)) {
    checkElement(node.openingElement, node.children, source);
  } else if (ts.isJsxSelfClosingElement(node)) {
    checkElement(node, [], source);
  }
  ts.forEachChild(node, (child) => visit(child, source));
}

function checkElement(opening, children, source) {
  const tag = opening.tagName.getText(source);
  const attributes = new Map();
  for (const property of opening.attributes.properties) {
    if (ts.isJsxAttribute(property)) attributes.set(property.name.getText(source), property.initializer?.getText(source) ?? "");
  }

  const className = attributes.get("className") ?? "";
  if (tag === "button" && className.includes("icon-button")) {
    const hasAccessibleName = attributes.has("aria-label") || hasVisibleText(children, source);
    if (!hasAccessibleName) fail(opening, source, "纯图标按钮必须提供 aria-label");
  }

  if (tag === "img" && !attributes.has("alt")) {
    fail(opening, source, "图片必须提供 alt");
  }

  if (attributes.get("role")?.includes("dialog") && !attributes.has("aria-label") && !attributes.has("aria-labelledby")) {
    fail(opening, source, "对话框必须提供 aria-label 或 aria-labelledby");
  }
}

function hasVisibleText(children, source) {
  return children.some((child) => {
    if (ts.isJsxText(child)) return child.getText(source).trim().length > 0;
    if (ts.isJsxElement(child)) return child.children.some((nested) => ts.isJsxText(nested) && nested.getText(source).trim().length > 0);
    return false;
  });
}

function fail(node, source, message) {
  const position = source.getLineAndCharacterOfPosition(node.getStart(source));
  failures.push(`${path.relative(path.resolve(import.meta.dirname, ".."), source.fileName)}:${position.line + 1}: ${message}`);
}

function* walk(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) yield* walk(fullPath);
    else yield fullPath;
  }
}
