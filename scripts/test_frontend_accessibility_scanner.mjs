import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { inspectTypeScriptSources, checkJsxAccessibility } from "./lib/frontend-accessibility-jsx.mjs";

const root = path.resolve(import.meta.dirname, "../.codex-runtime/frontend-accessibility");
fs.mkdirSync(root, { recursive: true });
const directory = fs.mkdtempSync(path.join(root, "scanner-"));
const invalid = path.join(directory, "invalid.tsx"), valid = path.join(directory, "valid.tsx"), malformed = path.join(directory, "malformed.tsx");
try {
  fs.writeFileSync(invalid, `export const View = () => <>
    <button>保存</button>
    <button type="button" className="icon-button"><svg /></button>
    <img src="demo.png" />
    <div role="dialog" />
    <tr className="clickable-row" />
  </>;`);
  fs.writeFileSync(valid, `// <button>Comment is not an element</button>
    export const View = () => <>
      <button type="button" className="icon-button" aria-label="保存"><svg /></button>
      <button type="button" className="icon-button"><span>保存</span></button>
      <img src="demo.png" alt="" /><div role="dialog" aria-labelledby="heading" />
      <tr className="clickable-row" tabIndex={0} onKeyDown={() => {}} />
    </>;`);
  const results = new Map();
  inspectTypeScriptSources([invalid, valid], (file, source) => results.set(file, checkJsxAccessibility(source, directory)));
  assert.deepEqual(results.get(valid), []);
  const failures = results.get(invalid);
  assert.equal(failures.length, 5);
  for (const [index, text] of ["显式声明 type", "纯图标按钮", "图片必须提供 alt", "对话框必须提供", "可点击表格行"].entries()) {
    assert(failures[index].startsWith(`invalid.tsx:${index + 2}:`), failures[index]);
    assert(failures[index].includes(text), failures[index]);
  }
  fs.writeFileSync(malformed, "export const Broken = () => <button");
  assert.throws(() => inspectTypeScriptSources([malformed], () => assert.fail("malformed JSX must not be inspected as valid")), /malformed\.tsx/);
  assert.throws(() => inspectTypeScriptSources([path.join(directory, "missing.tsx")], () => assert.fail("missing source must not pass")), /could not load source/);
  console.log("Frontend accessibility scanner regression passed (five rules, valid JSX, parse failure, missing source).");
} finally {
  fs.rmSync(directory, { recursive: true, force: true });
}
