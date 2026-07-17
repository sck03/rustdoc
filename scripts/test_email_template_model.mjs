import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "email-template-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "features", "email-templates", "emailTemplateModel.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; globalThis.__model = model;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const model = globalThis.__model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };
const empty = model.createEmptyEmailTemplateDraft();
assert(empty.category === "通用" && empty.isActive === true && empty.isShared === false, "empty template defaults");
assert(model.areEmailTemplateDraftsEqual(empty, { ...empty }), "equal template drafts");
assert(!model.areEmailTemplateDraftsEqual(empty, { ...empty, subject: "Changed" }), "changed template draft");
assert(!model.areEmailTemplateDraftsEqual(empty, { ...empty, isShared: true }), "changed template sharing");
assert(model.createEmailTemplateCopyName("首次报价", []) === "首次报价 副本", "first copy name");
assert(model.createEmailTemplateCopyName("首次报价", ["首次报价 副本"]) === "首次报价 副本 2", "numbered copy name");
assert(model.createEmailTemplateCopyName("首次报价", ["首次报价 副本", "首次报价 副本 2"]) === "首次报价 副本 3", "next copy name");
process.stdout.write("email-template-model tests passed\n");
