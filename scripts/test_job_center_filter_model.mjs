import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import assert from "node:assert/strict";

const require = createRequire(import.meta.url);
const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repositoryRoot, ".codex-runtime", "job-center-filter-model-test");
const entryPath = path.join(workspace, "entry.ts");
const bundlePath = path.join(workspace, "bundle.mjs");
const modelPath = path.join(repositoryRoot, "apps", "export-doc-web", "src", "features", "jobs", "jobPresentation.ts");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });
fs.writeFileSync(entryPath, `export { commitJobCenterFilters, hasPendingJobCenterFilters } from ${JSON.stringify(modelPath.replaceAll("\\", "/"))};`, "utf8");
const esbuild = require(path.join(repositoryRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entryPath], outfile: bundlePath, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
const model = await import(`${pathToFileURL(bundlePath).href}?v=${Date.now()}`);

assert.deepEqual(model.commitJobCenterFilters(" reportpdf-74ef575a896346baa0c61d97cb5a7588 ", ""), {
  keyword: "reportpdf-74ef575a896346baa0c61d97cb5a7588",
  committedKeyword: "reportpdf-74ef575a896346baa0c61d97cb5a7588",
  status: "",
  pageNumber: 1,
}, "清空或提交搜索词必须规范化并回到第一页");
assert.equal(model.commitJobCenterFilters("", "Failed").status, "Failed", "提交状态筛选必须保留状态");
assert.equal(model.hasPendingJobCenterFilters("", "", 1), false, "相同的全部筛选不应被视为待提交");
assert.equal(model.hasPendingJobCenterFilters("", "旧任务", 1), true, "清空搜索词必须被识别为待提交");
assert.equal(model.hasPendingJobCenterFilters("旧任务", "旧任务", 2), true, "切换页码后刷新必须回到第一页");
process.stdout.write("job-center-filter-model tests passed\n");
