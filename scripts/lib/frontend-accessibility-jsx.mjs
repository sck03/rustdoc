import path from "node:path";
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";

// Resolve public API exports from the exact TypeScript version used to build the app.
const require = createRequire(new URL("../../apps/export-doc-web/package.json", import.meta.url));
const { API } = await import(pathToFileURL(require.resolve("typescript/unstable/sync")).href);
const ts = await import(pathToFileURL(require.resolve("typescript/unstable/ast")).href);

export function inspectTypeScriptSources(files, inspect) {
  const api = new API({ cwd: path.resolve(import.meta.dirname, "../../apps/export-doc-web") });
  let snapshot;
  try {
    snapshot = api.updateSnapshot({ openFiles: files });
    for (const file of files) {
      const program = snapshot.getDefaultProjectForFile(file)?.program;
      const source = program?.getSourceFile(file);
      if (!source) throw new Error(`Accessibility scan could not load source: ${file}`);
      const diagnostics = program.getSyntacticDiagnostics(file);
      if (diagnostics.length) throw new Error(`${file}: ${diagnostics.map(diagnostic => diagnostic.text).join("; ")}`);
      inspect(file, source);
    }
  } finally {
    try { snapshot?.dispose(); } finally { api.close(); }
  }
}

export function checkJsxAccessibility(source, repositoryRoot) {
  const failures = [];
  const fail = (node, message) => {
    const position = source.getLineAndCharacterOfPosition(node.getStart(source));
    failures.push(`${path.relative(repositoryRoot, source.fileName)}:${position.line + 1}: ${message}`);
  };
  function checkElement(opening, children) {
    const tag = opening.tagName.getText(source);
    const attributes = new Map();
    for (const property of opening.attributes.properties) {
      if (ts.isJsxAttribute(property)) attributes.set(property.name.getText(source), property.initializer?.getText(source) ?? "");
    }
    const className = attributes.get("className") ?? "";
    if (className.includes("clickable-row") && (!attributes.has("tabIndex") || !attributes.has("onKeyDown"))) {
      fail(opening, "可点击表格行必须提供键盘焦点和 Enter/空格操作，避免只能用鼠标打开");
    }
    if (tag === "button" && !attributes.has("type")) fail(opening, "原生按钮必须显式声明 type，防止表单内误提交");
    if (tag === "button" && className.includes("icon-button") && !attributes.has("aria-label") && !hasVisibleText(children)) {
      fail(opening, "纯图标按钮必须提供 aria-label");
    }
    if (tag === "img" && !attributes.has("alt")) fail(opening, "图片必须提供 alt");
    if (attributes.get("role")?.includes("dialog") && !attributes.has("aria-label") && !attributes.has("aria-labelledby")) {
      fail(opening, "对话框必须提供 aria-label 或 aria-labelledby");
    }
  }
  function hasVisibleText(children) {
    return children.some(child => {
      if (ts.isJsxText(child)) return child.getText(source).trim().length > 0;
      return ts.isJsxElement(child) && child.children.some(nested => ts.isJsxText(nested) && nested.getText(source).trim().length > 0);
    });
  }
  function visit(node) {
    if (ts.isJsxElement(node)) checkElement(node.openingElement, node.children);
    else if (ts.isJsxSelfClosingElement(node)) checkElement(node, []);
    node.forEachChild(visit);
  }
  visit(source);
  return failures;
}
