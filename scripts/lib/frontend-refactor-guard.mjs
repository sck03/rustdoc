import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { walkSourceFiles } from "./source-files.mjs";
const root=path.resolve(import.meta.dirname,"../..");
const read=file=>readFileSync(path.join(root,file),"utf8");
const prefix="apps/export-doc-web/";
const dependencies=Object.keys(JSON.parse(read(`${prefix}package.json`)).dependencies).sort();
assert.deepEqual(dependencies,["@tanstack/react-query","@tauri-apps/api","lucide-react","react","react-dom","react-router-dom","three"].sort(),"新增运行/UI 依赖须按架构单独审查。");
assert(read(`${prefix}src/main.tsx`).includes('import "./styles/index.css"'),"应用必须从共享样式入口启动。");
for (const absolute of walkSourceFiles(path.join(root, prefix, "src"))) {
  const file = path.relative(root, absolute).replaceAll(path.sep, "/");
  if (/\.tsx?$/u.test(file) && !file.endsWith("/ui/browserStorage.ts")) {
    assert(!/\blocalStorage\b/u.test(read(file)),`${file}: 禁止新增 LocalStorage 持久化。`);
  }
}
console.log("Frontend refactor guard: dependency boundary, shared entry and managed persistence passed. API projections are verified by generate-api-client.ps1 -Check.");
