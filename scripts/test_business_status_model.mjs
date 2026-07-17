import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "business-status-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "ui", "businessStatusModel.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; globalThis.__model = model;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const { getBusinessStatusTone } = globalThis.__model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };
assert(getBusinessStatusTone("已成交") === "positive", "closed-won status tone");
assert(getBusinessStatusTone("已完成") === "positive", "completed follow-up status tone");
assert(getBusinessStatusTone("合作中") === "positive", "active supplier status tone");
assert(getBusinessStatusTone("谈判中") === "warning", "negotiation status tone");
assert(getBusinessStatusTone("待跟进") === "warning", "pending follow-up status tone");
assert(getBusinessStatusTone("已失单") === "danger", "closed-lost status tone");
assert(getBusinessStatusTone("已逾期") === "danger", "overdue follow-up status tone");
assert(getBusinessStatusTone("停用") === "muted", "inactive status tone");
assert(getBusinessStatusTone("线索") === "info", "lead status tone");
assert(getBusinessStatusTone("未知状态") === "info", "unknown status fallback tone");
process.stdout.write("business-status-model tests passed\n");
